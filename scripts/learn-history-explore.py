#!/usr/bin/env python3
"""Exploratory aggregate relationships from a frozen private historical statistics run.
No feature selection, policy fitting, causal claims, p-value search or individual-row export.
"""
import argparse
import collections
import importlib.util
import json
import os
from pathlib import Path
import sys
import numpy as np
import scipy
from scipy import stats

spec=importlib.util.spec_from_file_location('history_stats',Path(__file__).with_name('learn-history-statistics.py'))
s=importlib.util.module_from_spec(spec);spec.loader.exec_module(s)
h=s.h
FEATURES=['H']+['D'+str(i) for i in range(1,9)]+['log1pTextWords','log1pTextCharacters','safetyFloor']
ENDPOINTS=['responseEnvelopeThroughNativeCloseSeconds','responseEnvelopeUnionSeconds','gapCappedUnionSeconds3600','gapCappedUnionSeconds28800','dispatchToCloseSeconds']
POST=['nativeCommentCount','reviewDeclarations','changesRequiredDeclarations','reopenEvents','crossReferencedPRs','invocationCount','workerInvocations','criticInvocations','responseCount']


def feature(row,name):
    if name.startswith('D') and name[1:].isdigit():return row.get('baselineDimensions',{}).get(name)
    if name=='log1pTextWords':return np.log1p(row['baselineTextWords']) if row.get('baselineTextWords') is not None else None
    if name=='log1pTextCharacters':return np.log1p(row['baselineTextCharacters']) if row.get('baselineTextCharacters') is not None else None
    if name=='safetyFloor':return int(row['baselineSafetyFloor']) if row.get('baselineSafetyFloor') is not None else None
    return row.get(name)


def pair(rows,a,b):
    pairs=[(feature(r,a),feature(r,b)) for r in rows]
    known=[(x,y) for x,y in pairs if x is not None and y is not None]
    x=[v[0] for v in known];y=[v[1] for v in known]
    return {'n':len(known),'missing':len(rows)-len(known),'xUnique':len(set(x)),'yUnique':len(set(y)),
            'spearman':s.coefficient(x,y,'spearman'),'kendall':s.coefficient(x,y,'kendall')}


def partial_rank(rows,predictor,endpoint,control_repository=True):
    rows=[r for r in rows if feature(r,predictor) is not None and r.get(endpoint) is not None and feature(r,'log1pTextWords') is not None]
    if len(rows)<8:return {'n':len(rows),'status':'insufficient rows'}
    repos=sorted({r['repository'] for r in rows})
    columns=[np.ones(len(rows)),stats.rankdata([feature(r,'log1pTextWords') for r in rows])]
    if control_repository:columns.extend(np.array([int(r['repository']==repo) for r in rows]) for repo in repos[1:])
    X=np.column_stack(columns);rank=int(np.linalg.matrix_rank(X));df=len(rows)-rank-1
    a=stats.rankdata([feature(r,predictor) for r in rows]);b=stats.rankdata([r[endpoint] for r in rows])
    ar=a-X@np.linalg.lstsq(X,a,rcond=None)[0];br=b-X@np.linalg.lstsq(X,b,rcond=None)[0]
    return {'n':len(rows),'designRank':rank,'residualDegreesOfFreedom':df,'clusters':len({r['cluster'] for r in rows}),
            'partialRankCorrelation':float(np.corrcoef(ar,br)[0,1]) if df>=8 and np.std(ar)>1e-8 and np.std(br)>1e-8 else None,
            'controls':['rank baseline text words']+(['repository indicators'] if control_repository else []),
            'interpretation':'descriptive rank residual association; no causal adjustment, no small-cluster CI'}


def ridge_predict(train,test,features,alpha=10.):
    def matrix(rows):return np.array([[feature(r,k) for k in features] for r in rows],dtype=float)
    y=np.log1p([r['responseEnvelopeUnionSeconds'] for r in train])
    if not features:return np.repeat(np.mean(y),len(test))
    X=matrix(train);T=matrix(test);mu=X.mean(axis=0);sd=X.std(axis=0);sd[sd==0]=1
    X=(X-mu)/sd;T=(T-mu)/sd
    beta=np.linalg.solve(X.T@X+alpha*np.eye(len(features)),X.T@(y-y.mean()))
    return y.mean()+T@beta


def grouped_prediction(rows, include_sensitivity=True):
    rows=[r for r in rows if r.get('responseEnvelopeUnionSeconds') is not None and all(feature(r,k) is not None for k in FEATURES)]
    groups=sorted({r['cluster'] for r in rows})
    models={'intercept_mean':[],'intercept_median':None,'H':['H'],'text_length':['log1pTextWords'],'D1_to_D8':['D'+str(i) for i in range(1,9)],'risk_evidence_D4_D8':['D4','D8'],'demand_D1_D2_D3_D5_D6':['D1','D2','D3','D5','D6']}
    result={'n':len(rows),'groups':len(groups),'split':'leave one observed native parent component out','ridgeAlpha':10,
            'selection':'all declared models reported; no tuned alpha or selected winner; exploratory reuse of this cohort',
            'models':{}}
    if len(groups)<4:return dict(result,status='insufficient groups')
    for model,features in models.items():
        errors=[];fold_errors=[];fold_details=[]
        for group in groups:
            train=[r for r in rows if r['cluster']!=group];test=[r for r in rows if r['cluster']==group]
            prediction=np.repeat(np.median(np.log1p([r['responseEnvelopeUnionSeconds'] for r in train])),len(test)) if features is None else ridge_predict(train,test,features);truth=np.log1p([r['responseEnvelopeUnionSeconds'] for r in test])
            err=prediction-truth;errors.extend(err);fold_errors.append(float(np.mean(abs(err))))
            fold_details.append({'fold':len(fold_details)+1,'trainN':len(train),'testN':len(test),'logMAE':float(np.mean(abs(err))),'logRMSE':float(np.sqrt(np.mean(err**2)))})
        result['models'][model]={'pooledLogMAE':float(np.mean(np.abs(errors))),'pooledLogRMSE':float(np.sqrt(np.mean(np.square(errors)))),
                                'equalClusterMeanLogMAE':float(np.mean(fold_errors)),'foldLogMAERange':[min(fold_errors),max(fold_errors)],'folds':fold_details}
    # Temporal cut is outcome-blind; all tasks in crossing clusters are purged.
    ordered=sorted(h.instant(r['baselineBoundary']).timestamp() for r in rows);cut=float(np.median(ordered))
    train_groups=[];test_groups=[];crossing=[]
    for g in groups:
        times=[h.instant(r['baselineBoundary']).timestamp() for r in rows if r['cluster']==g]
        (train_groups if max(times)<cut else test_groups if min(times)>=cut else crossing).append(g)
    train=[r for r in rows if r['cluster'] in train_groups];test=[r for r in rows if r['cluster'] in test_groups]
    temporal={'cutoffEpochSeconds':cut,'trainGroups':len(train_groups),'testGroups':len(test_groups),'purgedCrossingGroups':len(crossing),'trainRows':len(train),'testRows':len(test),'models':{}}
    if len(train_groups)>=3 and len(test_groups)>=2 and len(train)>=10 and len(test)>=5:
        for model,features in models.items():
            pred=np.repeat(np.median(np.log1p([r['responseEnvelopeUnionSeconds'] for r in train])),len(test)) if features is None else ridge_predict(train,test,features);truth=np.log1p([r['responseEnvelopeUnionSeconds'] for r in test]);temporal['models'][model]={'logMAE':float(np.mean(abs(pred-truth)))}
    else:temporal['status']='unsupported after enforcing chronology and parent separation; no temporal score reported'
    result['temporalHoldout']=temporal
    if include_sensitivity:
        counts=collections.Counter(r['cluster'] for r in rows);largest=max(counts.values());removed={g for g,n in counts.items() if n==largest}
        result['leaveLargestParentSensitivity']={'removedGroups':len(removed),'removedRows':sum(counts[g] for g in removed),'tieRule':'remove all tied largest groups','evaluation':grouped_prediction([r for r in rows if r['cluster'] not in removed],False)}
    return result


def aggregate_strata(rows,field,minimum=5):
    groups=collections.defaultdict(list)
    for row in rows:groups[row[field]].append(row)
    output=[]
    for key,group in sorted(groups.items()):
        if len(group)<minimum:continue
        closed=[r for r in group if r['state']=='closed']
        output.append({'stratum':key,'n':len(group),'open':len(group)-len(closed),'closed':len(closed),
                       'closedCalendar':s.describe([r['calendarSeconds'] for r in closed]),'openAge':s.describe([r['calendarSeconds'] for r in group if r['state']=='open'])})
    return output


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--statistics-dir',type=Path,required=True);p.add_argument('--inventory-dir',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();os.umask(0o077)
    if a.output.exists():raise ValueError('output exists; preserve prior run')
    manifest,census,sources=h.inventory(a.inventory_dir)
    rowpath=a.statistics_dir/'PRIVATE-rows.json';aggregatepath=a.statistics_dir/'aggregate.json';rows=json.loads(rowpath.read_text());base=json.loads(aggregatepath.read_text())
    sources.extend({'path':str(path),'sha256':h.digest(path.read_bytes())} for path in [rowpath,aggregatepath,Path(__file__),Path(s.__file__),Path(h.__file__)])
    if len({r['itemId'] for r in rows}) != len(rows) or len(rows) != base['cohort']['qualifiedHistories']:raise ValueError('private row identity/count mismatch')
    runtime=[r for r in rows if r.get('responseEnvelopeUnionSeconds') is not None]
    if len(runtime) != base['cohort']['withResponseTimelines'] or sum(r['responseCount'] for r in runtime) != base['cohort']['responses']:raise ValueError('runtime row counts disagree with frozen statistics aggregate')
    creation=[dict(r,H=r['creationH'],baselineDimensions=r.get('creationDimensions',{}),baselineTextWords=r.get('creationTextWords'),baselineTextCharacters=r.get('creationTextCharacters'),baselineSafetyFloor=r.get('creationSafetyFloor')) for r in rows]
    support={f:{'known':sum(feature(r,f) is not None for r in runtime),'values':dict(sorted(collections.Counter(str(feature(r,f)) for r in runtime if feature(r,f) is not None).items()))} for f in FEATURES if not f.startswith('log')}
    dimensions={f:{e:pair(runtime,f,e) for e in ENDPOINTS} for f in FEATURES}
    cooccurrence={a:{b:pair(runtime,a,b) for b in FEATURES} for a in FEATURES}
    post={f:pair(runtime,f,'responseEnvelopeUnionSeconds') for f in POST}
    profiles=sorted({r.get('workerProfile') for r in runtime if r.get('workerProfile')})
    profile_results={profile:{f:pair([r for r in runtime if r.get('workerProfile')==profile],f,'workerEnvelopeUnionSeconds') for f in FEATURES} for profile in profiles}
    grouped=[]
    for source in census:
        item=source['repository']+'#'+str(source['number']);end=h.instant(source['closed_at'] or manifest['cutoff'])
        grouped.append({'kind':source['kind'],'state':source['state'],'stateReason':str(source['state_reason']),'repository':source['repository'] if not source['private'] else 'private repositories pooled',
            'era':source['created_at'][:7],'authorType':source['authorType'] or 'Unknown',
            'currentHeader':'Delivery header' if 'Work level: Delivery' in (source['body'] or '') else 'Outcome header' if 'Work level: Outcome' in (source['body'] or '') else 'no exact header',
            'calendarSeconds':(end-h.instant(source['created_at'])).total_seconds(),'inRuntimeCohort':item in {r['itemId'] for r in runtime},'currentBodyWords':len((source['body'] or '').split()),'currentCommentCount':source['comments']})
    broad={kind:{field:aggregate_strata([r for r in grouped if r['kind']==kind],field) for field in ('repository','era','authorType','currentHeader','stateReason')} for kind in ('issue','pull_request')}
    selection={}
    issues=[r for r in grouped if r['kind']=='issue']
    for field in ('era','repository','state','currentHeader','authorType'):
        strata=collections.defaultdict(list)
        for r in issues:strata[r[field]].append(r)
        selection[field]=[{'stratum':k,'population':len(g),'runtime':sum(r['inRuntimeCohort'] for r in g),'fraction':sum(r['inRuntimeCohort'] for r in g)/len(g)} for k,g in sorted(strata.items())]
    # Nonlinearity summaries retain each supported H level; no data-driven cut search.
    levels=[]
    for value in sorted({r['H'] for r in runtime if r['H'] is not None}):
        group=[r for r in runtime if r['H']==value]
        levels.append({'H':value,'n':len(group),'duration':s.describe([r['responseEnvelopeUnionSeconds'] for r in group])})
    out={'schema':'fsgg.learn-history-exploration/1','versions':base['versions'],'cohort':base['cohort'],
         'creationBodyCalendarAssociations':{f:pair(creation,f,'calendarClosedSeconds') for f in FEATURES},'featureSupport':support,'dimensionEndpointAssociations':dimensions,'featureCooccurrence':cooccurrence,
         'withinWorkerProfileAssociations':profile_results,'postdispatchDiagnostics':post,
         'partialRankAssociations':{f:partial_rank(runtime,f,'responseEnvelopeUnionSeconds') for f in ['H']+['D'+str(i) for i in range(1,9)]},
         'hardnessLevelSummaries':levels,'groupedPrediction':grouped_prediction(runtime),'broadStrata':broad,'runtimeSelection':selection,
         'limitations':['Exploratory 12 baseline features by 5 endpoints, profile and partial checks: multiplicity and selection preclude confirmatory claims; no p-values or selected winner.',
          'All clocks retain original proxy/censoring qualifications. Postdispatch comments/reviews/PR references/response counts are mediators or mechanically coupled diagnostics, not prework predictors.',
          'Current exact-header/type signals in broad census are retrospective and incomplete; no historical task class inferred.',
          'Group CV uses fixed alpha10 and fold-local standardization; seven clusters give unstable generalization evidence. This is not an untouched final test.',
          'Sparse H-level summaries are not calibrated thresholds; zero-variance dimensions are not evidence of no effect.'],
         'sourceManifestDigest':h.digest(h.canonical(sources))}
    a.output.mkdir(mode=0o700,parents=True)
    (a.output/'aggregate.json').write_text(json.dumps(out,indent=2,allow_nan=False)+'\n');(a.output/'PRIVATE-source-manifest.json').write_text(json.dumps(sources,indent=2)+'\n')
    print(json.dumps({'runtimeItems':len(runtime),'baselineFeatures':len(FEATURES),'endpoints':len(ENDPOINTS),'output':str(a.output)}))

if __name__=='__main__':main()
