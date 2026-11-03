#!/usr/bin/env python3
"""Finite POSTHOC discovery grid on frozen private rows; never a validation test.
No native/network reads, causal adjustment, inferred pauses, p-values or selected winner.
"""
import argparse
import collections
import hashlib
import gzip
import json
import os
from pathlib import Path
import platform
import resource
import time

# Set before importing numerical libraries; every operation runs in this process.
for variable in ('OPENBLAS_NUM_THREADS', 'OMP_NUM_THREADS', 'MKL_NUM_THREADS', 'NUMEXPR_NUM_THREADS'):
    os.environ[variable] = '1'
import numpy as np
import scipy
from scipy import stats

SEED = 527603
# Fixed finite grid declared in source BEFORE the first private execution.
GRID = {
    'contexts': ['dispatch', 'creation'],
    'cohorts': ['all_observed', 'native_closed', 'native_completed'],
    'predictors': ['H', 'text'] + ['D'+str(i) for i in range(1, 9)],
    'controls': [[], ['text'], ['era', 'unit', 'repository'], ['text', 'era', 'unit', 'repository']],
    'dispatch_endpoints': ['responseEnvelopeUnionSeconds', 'responseEnvelopeThroughNativeCloseSeconds',
                           'dispatchToCloseSeconds', 'gapCappedUnionSeconds3600', 'gapCappedUnionSeconds28800',
                           'gapCappedUnionSeconds86400', 'activeExecutionSeconds'],
    'creation_endpoints': ['calendarObservedSeconds', 'calendarClosedSeconds'],
    'deletions': ['each_parent', 'each_repository', 'each_unit_signal', 'each_observed_worker_profile',
                  'each_creation_month', 'any_MAD_flag', 'any_1h_gap_cap_change', 'explicit_stop'],
    'prediction_models': ['mean', 'median', 'H', 'text', 'text_plus_H', 'D1_to_D8'],
    'prediction_scenarios': ['full', 'drop_largest_parent', 'drop_any_MAD_flag', 'drop_any_1h_gap_cap_change'],
    'minimum_rows': 8, 'minimum_clusters': 3, 'minimum_residual_df': 8,
    'ridge_alpha': 10.0, 'bootstrap_draws': 199,
}


def digest(b):
    return hashlib.sha256(b).hexdigest()


def unit(row, context):
    p = 'baseline' if context == 'dispatch' else 'creation'
    if row.get(p+'DeliveryHeader') is True:
        return 'delivery_header'
    if row.get(p+'OutcomeHeader') is True:
        return 'outcome_header'
    if p+'DeliveryHeader' not in row:
        return 'unknown'
    return 'neither_exact_header'


def value(row, key, context):
    p = 'baseline' if context == 'dispatch' else 'creation'
    if key == 'H':
        return row.get('H' if context == 'dispatch' else 'creationH')
    if key == 'text':
        x = row.get(p+'TextWords')
        return None if x is None else float(np.log1p(x))
    if key.startswith('D') and key[1:].isdigit():
        return row.get(p+'Dimensions', {}).get(key)
    return row.get(key)


def designs(rows, controls, context):
    columns = [np.ones(len(rows))]
    for key in controls:
        if key == 'text':
            columns.append(stats.rankdata([value(r, 'text', context) for r in rows]))
        else:
            vals = [unit(r, context) if key == 'unit' else r[key] for r in rows]
            columns.extend(np.array([v == level for v in vals], float) for level in sorted(set(vals))[1:])
    return np.column_stack(columns)


def association(rows, predictor, endpoint, controls, context):
    eligible = [r for r in rows if value(r, predictor, context) is not None and r.get(endpoint) is not None
                and ('text' not in controls or value(r, 'text', context) is not None)]
    n = len(eligible)
    out = {'n': n, 'missing': len(rows)-n, 'clusters': len({r['cluster'] for r in eligible})}
    if predictor in controls:
        return dict(out, status='unsupported: predictor also controlled')
    if n < GRID['minimum_rows'] or out['clusters'] < GRID['minimum_clusters']:
        return dict(out, status='unsupported: row or cluster support')
    x = stats.rankdata([value(r, predictor, context) for r in eligible])
    y = stats.rankdata([r[endpoint] for r in eligible])
    X = designs(eligible, controls, context)
    rank = int(np.linalg.matrix_rank(X))
    out.update(designRank=rank, residualDF=n-rank-1)
    a = x-X@np.linalg.lstsq(X, x, rcond=None)[0]
    b = y-X@np.linalg.lstsq(X, y, rcond=None)[0]
    if out['residualDF'] < GRID['minimum_residual_df'] or np.std(a) < 1e-8 or np.std(b) < 1e-8:
        return dict(out, status='unsupported: residual variation or degrees of freedom')
    return dict(out, status='descriptive', rankCorrelation=float(np.corrcoef(a, b)[0, 1]))


def scenarios(rows, context):
    result = [('full', rows, None)]
    group_fields = [('parent', lambda r:r['cluster']), ('repository', lambda r:r['repository']),
                    ('unit_signal', lambda r:unit(r, context)), ('observed_worker_profile', lambda r:r.get('workerProfile') or 'unknown'),
                    ('creation_month', lambda r:r['era'])]
    for label, fn in group_fields:
        for i, group in enumerate(sorted({fn(r) for r in rows}), 1):
            result.append((f'drop_{label}_{i:02}', [r for r in rows if fn(r) != group], None))
    result += [('drop_any_MAD_flag', [r for r in rows if not any(v is True for k,v in r.items() if k.endswith('MADFlag'))], None),
               ('drop_any_1h_gap_cap_change', [r for r in rows if r.get('gapCappedUnionSeconds3600') == r.get('responseEnvelopeUnionSeconds')], None),
               ('drop_explicit_stop', rows, 'unsupported: no explicit-stop field in frozen rows')]
    counts = collections.Counter(r['cluster'] for r in rows)
    largest = max(counts.values())
    result.append(('drop_largest_parent', [r for r in rows if counts[r['cluster']] != largest], None))
    return result


def cohort(rows, name):
    if name == 'native_closed':
        return [r for r in rows if r['state'] == 'closed']
    if name == 'native_completed':
        return [r for r in rows if r['state'] == 'closed' and r.get('stateReason') == 'completed']
    return rows


def predict(rows, endpoint, context):
    features = ['H', 'text'] + ['D'+str(i) for i in range(1,9)]
    eligible = [r for r in rows if r.get(endpoint) is not None and all(value(r,k,context) is not None for k in features)]
    groups = sorted({r['cluster'] for r in eligible})
    out = {'n':len(eligible), 'missing':len(rows)-len(eligible), 'clusters':len(groups), 'models':{}}
    if len(groups) < 4 or len(eligible) < 12:
        return dict(out, status='unsupported: fewer than four groups or twelve complete rows')
    models = {'mean':[], 'median':None, 'H':['H'], 'text':['text'], 'text_plus_H':['text','H'], 'D1_to_D8':features[2:]}
    for model, cols in models.items():
        errors=[]; folds=[]
        for g in groups:
            train=[r for r in eligible if r['cluster'] != g]; test=[r for r in eligible if r['cluster'] == g]
            y=np.log1p([r[endpoint] for r in train]); truth=np.log1p([r[endpoint] for r in test])
            if not cols:
                pred=np.repeat(np.median(y) if cols is None else np.mean(y), len(test))
            else:
                X=np.array([[value(r,k,context) for k in cols] for r in train]); T=np.array([[value(r,k,context) for k in cols] for r in test])
                mu=X.mean(0); sd=X.std(0); sd[sd == 0]=1
                X=(X-mu)/sd; T=(T-mu)/sd
                beta=np.linalg.solve(X.T@X+GRID['ridge_alpha']*np.eye(len(cols)), X.T@(y-y.mean()))
                pred=y.mean()+T@beta
            err=pred-truth; errors.extend(err.tolist()); folds.append({'trainN':len(train),'testN':len(test),'logMAE':float(np.mean(abs(err)))})
        out['models'][model]={'pooledLogMAE':float(np.mean(np.abs(errors))), 'equalClusterLogMAE':float(np.mean([f['logMAE'] for f in folds])), 'folds':folds}
    return dict(out, status='exploratory grouped reuse; no untouched test')


def bootstrap(rows, predictor, endpoint, context):
    # Whole-parent resampling, no row iid CI. Descriptive percentile ranges only.
    rows=[r for r in rows if r.get(endpoint) is not None and value(r,predictor,context) is not None]
    groups=sorted({r['cluster'] for r in rows}); rng=np.random.default_rng(SEED)
    out={'n':len(rows),'clusters':len(groups),'draws':GRID['bootstrap_draws']}
    if len(groups)<4 or len(rows)<12:
        return dict(out,status='unsupported: bootstrap support')
    vals=[]
    for _ in range(GRID['bootstrap_draws']):
        sample=[r for g in rng.choice(groups,len(groups),replace=True) for r in rows if r['cluster']==g]
        x=[value(r,predictor,context) for r in sample]; y=[r[endpoint] for r in sample]
        if len(set(x))>1 and len(set(y))>1:
            vals.append(float(stats.spearmanr(x,y).statistic))
    return dict(out, status='descriptive cluster percentile range; few clusters limit coverage', validDraws=len(vals),
                percentile95=[float(x) for x in np.quantile(vals,[.025,.975])] if vals else None)


def self_test():
    rows=[]
    for i in range(24):
        rows.append({'cluster':str(i//4),'repository':'r','era':'2026-01','H':i,'baselineTextWords':i+1,
                     'baselineDimensions':{'D1':1},'y':i*3,'state':'open'})
    assert association(rows,'H','y',[],'dispatch')['rankCorrelation'] > .999
    assert 'unsupported' in association(rows,'D1','y',[],'dispatch')['status']
    assert 'unsupported' in association(rows,'H','y',['text'],'dispatch')['status']
    assert association(rows[:4],'H','y',[],'dispatch')['missing']==0
    assert len(cohort(rows,'all_observed'))==24 and not cohort(rows,'native_completed')
    missing=[dict(r,y=None) for r in rows]
    assert association(missing,'H','y',[],'dispatch')['missing']==24
    # Positive predictive control plus a constant-feature negative control on identical grouped folds.
    synthetic=[dict(r,baselineDimensions={f'D{k}':1 for k in range(1,9)}) for r in rows]
    p=predict(synthetic,'y','dispatch')
    assert p['models']['H']['pooledLogMAE'] < p['models']['mean']['pooledLogMAE']
    assert p['models']['D1_to_D8']['pooledLogMAE'] == p['models']['mean']['pooledLogMAE']
    assert all(f['trainN']==20 and f['testN']==4 for f in p['models']['H']['folds'])
    assert bootstrap(rows,'H','y','dispatch') == bootstrap(rows,'H','y','dispatch')
    deletion=dict((name,subset) for name,subset,_ in scenarios(rows,'dispatch'))
    assert not deletion['drop_largest_parent']  # All six equal-size parents are removed together.
    assert next(reason for name,_,reason in scenarios(rows,'dispatch') if name=='drop_explicit_stop').startswith('unsupported')
    print('synthetic controls passed: monotonic signal, constant feature, collinearity, missingness, open retention, grouped prediction')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rows',type=Path); parser.add_argument('--output',type=Path); parser.add_argument('--self-test',action='store_true')
    args=parser.parse_args()
    if args.self_test:
        self_test(); return
    if args.rows is None or args.output is None:
        parser.error('--rows and --output required')
    if args.output.exists():
        raise ValueError('output exists; preserve prior runs')
    os.umask(0o077); resource.setrlimit(resource.RLIMIT_AS,(2*1024**3,2*1024**3)); resource.setrlimit(resource.RLIMIT_CPU,(240,240))
    start=time.monotonic(); raw=args.rows.read_bytes(); rows=json.loads(raw)
    if len({r['itemId'] for r in rows}) != len(rows):
        raise ValueError('duplicate item identity')
    comparisons=[]; predictions=[]; intervals=[]; coverage={}
    for context in GRID['contexts']:
        base=rows if context=='creation' else [r for r in rows if r.get('baselineBoundary') is not None]
        endpoints=GRID[context+'_endpoints']
        coverage[context]={'rows':len(base),'clusters':len({r['cluster'] for r in base}),
                           'states':dict(collections.Counter(r['state'] for r in base)),
                           'reasons':dict(collections.Counter(str(r.get('stateReason')) for r in base)),
                           'endpointKnown':{e:sum(r.get(e) is not None for r in base) for e in endpoints},
                           'workerProfileKnown':sum(r.get('workerProfile') is not None for r in base),
                           'gapDiagnosticKnown':sum(r.get('gapCappedUnionSeconds3600') is not None for r in base),
                           'MADDiagnosticKnown':sum(any(k.endswith('MADFlag') for k in r) for r in base)}
        for scenario, subset, failure in scenarios(base,context):
            for cname in GRID['cohorts']:
                selected=cohort(subset,cname)
                common={'context':context,'scenario':scenario,'cohort':cname,'scenarioN':len(subset),'cohortN':len(selected),'removedRows':len(base)-len(subset),'openRows':sum(r['state']=='open' for r in selected)}
                for endpoint in endpoints:
                    for predictor in GRID['predictors']:
                        for controls in GRID['controls']:
                            result=association(selected,predictor,endpoint,controls,context)
                            if failure:
                                result['status']=failure
                                result.pop('rankCorrelation',None)
                            comparisons.append(dict(common,endpoint=endpoint,predictor=predictor,controls=controls,**result))
                endpoint='responseEnvelopeUnionSeconds' if context=='dispatch' else 'calendarClosedSeconds'
                if scenario in GRID['prediction_scenarios']:
                    predictions.append(dict(common,endpoint=endpoint,**predict(selected,endpoint,context)))
                if scenario=='full':
                    for predictor in ['H','text']:
                        intervals.append(dict(common,endpoint=endpoint,predictor=predictor,**bootstrap(selected,predictor,endpoint,context)))
    out={'schema':'fsgg.learn-history-posthoc-multiverse/1','grid':GRID,'seed':SEED,'coverage':coverage,
         'versions':{'python':platform.python_version(),'numpy':np.__version__,'scipy':scipy.__version__},
         'sourceHashes':{'privateRows':digest(raw),'script':digest(Path(__file__).read_bytes())},
         'multiplicity':{'comparisons':len(comparisons),'descriptive':sum(c['status']=='descriptive' for c in comparisons),
                         'predictionEvaluations':len(predictions),'bootstrapEvaluations':len(intervals),'pValuesComputed':0},
         'comparisons':comparisons,'groupedPrediction':predictions,'clusterBootstrap':intervals,
         'limitations':['All choices and data reuse are POSTHOC discovery, including the finite grid; no causal or heldout claim.',
          'Calendar observed time includes open censor ages, not completed duration; native_closed includes not_planned; native_completed is a selected subset.',
          'Header signal and creation month are coarse observed baseline proxies, not adjudicated historical unit types. Worker profile and outcome-derived deletions are sensitivity strata, never causal baseline controls.',
          'Gap caps are hypothetical clock stress tests, not verified pauses. No explicit stop labels or verified active-execution endpoint exist in these rows.',
          'No p-values, significance threshold, equivalence claim, selected winner, multiplicity correction or policy activation. Every unsupported grid cell is retained.',
          'Bootstrap ranges are descriptive and unstable with few parent components. No adjusted-association CI is supplied; rank residual associations are not causal effects.',
          'Unknown gap/MAD diagnostics are retained during diagnostic deletions; coverage counts identify that missingness. Drop-largest removes all tied largest parents, even if this leaves unsupported support.',
          'No private individual IDs or repository names are exported. Anonymous group indices are deterministic within context; no linkage map is exported.'],
         'runtime':{'wallSeconds':time.monotonic()-start,'cpuLimitSeconds':240,'memoryLimitBytes':2*1024**3,'numericalThreads':1,'maxRSSKiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss}}
    args.output.mkdir(parents=True,mode=0o700)
    (args.output/'aggregate.json').write_text(json.dumps(out,indent=2,allow_nan=False)+'\n')
    # Deterministic, compressed JSON retains every cell without a large repetitive repository artifact.
    public=dict(out)
    public.pop('runtime')  # Machine-dependent timings remain in the private run receipt and report.
    with (args.output/'aggregate.json.gz').open('wb') as stream:
        stream.write(gzip.compress((json.dumps(public,separators=(',',':'),allow_nan=False)+'\n').encode(),mtime=0))
    print(json.dumps({'output':str(args.output),'coverage':coverage,'multiplicity':out['multiplicity'],'runtime':out['runtime']}))

if __name__=='__main__':
    main()
