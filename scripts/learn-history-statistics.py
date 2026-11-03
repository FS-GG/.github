#!/usr/bin/env python3
"""Descriptive historical duration statistics. No active-time or causal inference.
Run with the pinned requirements-learn-history.txt environment. Individual rows stay private.
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
from scipy import stats, optimize

spec = importlib.util.spec_from_file_location('history', Path(__file__).with_name('learn-history-evaluate.py'))
h = importlib.util.module_from_spec(spec)
spec.loader.exec_module(h)


def union_seconds(intervals):
    merged = []
    for a, b in sorted(intervals):
        if b < a:
            raise ValueError('negative interval')
        if merged and a <= merged[-1][1]:
            merged[-1][1] = max(b, merged[-1][1])
        else:
            merged.append([a, b])
    return sum(b-a for a, b in merged)


def intervals(times, cap=None):
    values = sorted(set(h.instant(t).timestamp() for t in times))
    if cap is None:
        return [(values[0], values[-1])] if values else []
    # Hypothetical work occurs at the beginning of each gap. Other placement can change unions.
    return [(a, min(b, a+cap)) for a, b in zip(values, values[1:])]


def describe(values):
    x = np.asarray(values, dtype=float)
    if not len(x):
        return {'n': 0}
    if np.any(x < 0) or not np.all(np.isfinite(x)):
        raise ValueError('invalid duration')
    log = np.log1p(x)
    med, mad = float(np.median(log)), float(stats.median_abs_deviation(log, scale=1))
    flags = np.abs(log-med) > 3*mad if mad > 0 else np.zeros(len(x), dtype=bool)
    return {'n': len(x), 'secondsQuantiles': dict(zip(('p50','p90','p99','max'), map(float, np.quantile(x,[.5,.9,.99,1])))),
            'log1pMedian': med, 'log1pUnscaledMAD': mad, 'threeMADFlags': int(sum(flags)),
            'flagRule': 'absolute log1p deviation > 3 unscaled MAD; diagnostic, no exclusion; MAD=0 gives no flags'}


def stratified_tails(rows, metric, field, minimum=5):
    groups=collections.defaultdict(list)
    for r in rows:
        if r.get(metric) is not None and r.get(field) is not None:groups[r[field]].append(r)
    residuals=[]
    for group in groups.values():
        if len(group)>=minimum:
            values=np.log1p([r[metric] for r in group]);residuals.extend(values-np.median(values))
    if not residuals:return {'eligibleRows':0,'status':'insufficient strata'}
    mad=float(stats.median_abs_deviation(residuals,scale=1))
    return {'eligibleRows':len(residuals),'eligibleStrata':sum(len(g)>=minimum for g in groups.values()),
            'minimumRowsPerStratum':minimum,'residualMAD':mad,
            'threeMADFlags':int(sum(abs(v)>3*mad for v in residuals)) if mad>0 else 0,
            'interpretation':'descriptive log-duration minus within-stratum median; not causal adjustment or exclusion'}


def coefficient(x, y, kind):
    if len(x)<3 or len(set(x))<2 or len(set(y))<2:
        return None
    return float((stats.spearmanr if kind=='spearman' else stats.kendalltau)(x,y).statistic)


def association(rows, metric, seed, repeats):
    r = [x for x in rows if x.get('H') is not None and x.get(metric) is not None]
    x = [v['H'] for v in r]; y = [v[metric] for v in r]
    groups = collections.defaultdict(list)
    for v in r:
        groups[v['cluster']].append(v)
    result = {'n': len(r), 'clusters': len(groups), 'hardnessValues': sorted(set(x)), 'duration': describe(y),
              'spearman': coefficient(x,y,'spearman'), 'kendall': coefficient(x,y,'kendall'),
              'asymptoticPValue': None, 'interpretation': 'selected observational cohort; no causal or invariant-hardness conclusion'}
    result['theilSenLog1pSlope'] = float(stats.theilslopes(np.log1p(y),x).slope) if len(set(x))>1 else None
    result['clusterBootstrap'] = {'seed': seed, 'resamples': repeats, 'unit': 'connected native parent component; other dependency links incompletely observed'}
    if len(groups)<8:
        result['clusterBootstrap']['status'] = 'not estimated: fewer than 8 observed clusters (audit guard, not universal sufficiency rule)'
        return result
    rng=np.random.default_rng(seed); keys=list(groups)
    for kind in ('spearman','kendall'):
        samples=[]
        for _ in range(repeats):
            sample=[v for k in rng.choice(keys,len(keys),replace=True) for v in groups[k]]
            z=coefficient([v['H'] for v in sample],[v[metric] for v in sample],kind)
            if z is not None and np.isfinite(z): samples.append(z)
        result['clusterBootstrap'][kind]={'validResamples':len(samples),'percentile95':list(map(float,np.quantile(samples,[.025,.975]))) if len(samples)>=.9*repeats else None}
    return result


def profile_interaction(rows, seed, repeats):
    rows=[r for r in rows if r.get('H') is not None and r.get('workerEnvelopeUnionSeconds') is not None and r.get('workerProfile')]
    profiles=sorted({r['workerProfile'] for r in rows})
    if len(profiles)!=2:
        return {'status':'not estimable: requires exactly two observed worker profiles'}
    support={p:sorted({r['H'] for r in rows if r['workerProfile']==p}) for p in profiles}
    overlap=sorted(set(support[profiles[0]]) & set(support[profiles[1]]))
    answer={'profiles':profiles,'hardnessSupport':support,'commonHardnessValues':overlap,'n':len(rows),'causal':False,
            'interpretation':'observational H-by-profile log-envelope association; role held to worker, task mix uncontrolled; effort contrast unavailable'}
    if len(overlap)<2:
        return dict(answer,status='not estimable: insufficient common hardness support')
    rows=[r for r in rows if r['H'] in overlap]
    center=float(np.mean([r['H'] for r in rows]))
    def fit(sample):
        X=np.array([[1,r['H']-center,int(r['workerProfile']==profiles[1]),(r['H']-center)*int(r['workerProfile']==profiles[1])] for r in sample])
        if np.linalg.matrix_rank(X)<4:return None
        y=np.log1p([r['workerEnvelopeUnionSeconds'] for r in sample])
        z=optimize.least_squares(lambda b:X@b-y,np.linalg.lstsq(X,y,rcond=None)[0],loss='huber',f_scale=1.0)
        return z.x if z.success else None
    result=fit(rows);answer.update(nCommonSupport=len(rows),hardnessCenter=center,coefficients=result.tolist() if result is not None else None,
        coefficientOrder=['intercept','H','secondProfile','H:secondProfile'],loss='Huber on log1p seconds, f_scale=1; does not identify why tails occur')
    groups=collections.defaultdict(list)
    for r in rows:groups[r['cluster']].append(r)
    answer['clusters']=len(groups)
    if result is not None and len(groups)>=8:
        rng=np.random.default_rng(seed);keys=list(groups);samples=[]
        for _ in range(repeats):
            z=fit([r for k in rng.choice(keys,len(keys),replace=True) for r in groups[k]])
            if z is not None:samples.append(z[3])
        answer['interactionBootstrap']={'validResamples':len(samples),'requestedResamples':repeats,'percentile95':np.quantile(samples,[.025,.975]).tolist() if len(samples)>=.9*repeats else None}
    return answer


def main():
    p=argparse.ArgumentParser(description=__doc__)
    for name in ('inventory-dir','first-dispatches','response-times','scorer','output'):p.add_argument('--'+name,type=Path,required=True)
    p.add_argument('--histories',type=Path,action='append',required=True)
    p.add_argument('--seed',type=int,default=5276);p.add_argument('--bootstrap',type=int,default=500)
    a=p.parse_args();os.umask(0o077)
    if a.output.exists():raise ValueError('preserve prior output: directory exists')
    if not 100<=a.bootstrap<=5000:raise ValueError('bootstrap bound 100..5000')
    manifest,census,sources=h.inventory(a.inventory_dir);cutoff=h.instant(manifest['cutoff'])
    indexed={r['node_id']:r for r in census};native={r['repository']+'#'+str(r['number']):r for r in census}
    histories={}
    for root in a.histories:
        mp=root/'audit-manifest.json';sources.append({'path':str(mp),'sha256':h.digest(mp.read_bytes())})
        for q in json.loads(mp.read_text())['queries']:
            if 'sha256' not in q:continue
            path=root/('audit-'+q['nodeId']+'.json');payload=path.read_bytes()
            if h.digest(payload)!=q['sha256']:raise ValueError('history digest mismatch')
            record=json.loads(payload)['data']['node']
            if record['id']!=q['nodeId']:raise ValueError('history identity mismatch')
            histories[record['id']]=record;sources.append({'path':str(path),'sha256':q['sha256']})
    dispatch={r['item_id']:r['first_dispatch'] for r in json.loads(a.first_dispatches.read_text())}
    response=json.loads(a.response_times.read_text());by_item=collections.defaultdict(list)
    seen=set()
    for r in response['rows']:
        if len(r['observedModels']) != 1 or len(r['observedEfforts']) != 1 or not all(isinstance(v,str) and v for v in r['observedModels']+r['observedEfforts']):raise ValueError('unknown or mixed invocation profile requires separate qualification')
        if r['invocationId'] in seen:raise ValueError('duplicate invocation')
        seen.add(r['invocationId'])
        if len(r['occurredAt'])!=r['responseCount'] or r['timestampedResponseCount']!=r['responseCount']:raise ValueError('timestamp count mismatch')
        by_item[r['itemId']].append(r)
    if sum(r['responseCount'] for r in response['rows'])!=response['responses']:raise ValueError('response count mismatch')
    parent={r['html_url']:r['html_url'] for r in census}
    def find(x):
        while parent[x]!=x:x=parent[x]
        return x
    for r in census:
        q=(r.get('parent_issue_url') or '').replace('https://api.github.com/repos/', 'https://github.com/')
        if q in parent:parent[find(r['html_url'])]=find(q)
    scorer=h.scoring_module(a.scorer);rows=[];unknown=collections.Counter()
    for node,record in histories.items():
        source=indexed[node]
        if source['kind']!='issue':continue
        item=source['repository']+'#'+str(source['number'])
        row={'itemId':item,'cluster':find(source['html_url']),'repository':source['repository'],'era':source['created_at'][:7],
             'state':source['state'],'stateReason':source['state_reason'],'H':None,'creationH':None,'currentH':None,'activeExecutionSeconds':None}
        for label,boundary in [('creationH',source['created_at']),('H',dispatch.get(item))]:
            if boundary:
                try:
                    body,provenance=h.body_at_boundary(record,boundary)
                    score=h.score_body(scorer,item,body,boundary);row[label]=score['hardness'];row[label+'Category']=score.get('hardnessCategory');row[label+'Provenance']=provenance
                except ValueError as exc:unknown[label+':'+str(exc)]+=1
        row['currentH']=h.score_body(scorer,item,record['body'],record['createdAt'])['hardness']
        end=h.instant(source['closed_at']) if source['closed_at'] else cutoff
        row['calendarObservedSeconds']=(end-h.instant(source['created_at'])).total_seconds()
        row['calendarClosedSeconds']=row['calendarObservedSeconds'] if source['state']=='closed' else None
        row['dispatchToCloseSeconds']=(end-h.instant(dispatch[item])).total_seconds() if item in dispatch and source['state']=='closed' else None
        if row['dispatchToCloseSeconds'] is not None and row['dispatchToCloseSeconds']<0:
            unknown['negative observed dispatch-to-close']+=1;row['dispatchToCloseSeconds']=None
        observed=by_item.get(item,[])
        if observed:
            all_intervals=[pair for inv in observed for pair in intervals(inv['occurredAt'])]
            row['responseEnvelopeUnionSeconds']=union_seconds(all_intervals)
            row['summedInvocationEnvelopeSeconds']=sum(b-a for a,b in all_intervals)
            row['responseEnvelopeThroughNativeCloseSeconds']=union_seconds([(a,min(b,end.timestamp())) for a,b in all_intervals if a<=end.timestamp()]) if source['state']=='closed' else None
            row['responseCount']=sum(inv['responseCount'] for inv in observed)
            row['responsesAfterNativeClose']=sum(h.instant(t)>end for inv in observed for t in inv['occurredAt']) if source['state']=='closed' else None
            for cap in (3600,28800,86400):row['gapCappedUnionSeconds'+str(cap)]=union_seconds([pair for inv in observed for pair in intervals(inv['occurredAt'],cap)])
            workers=[inv for inv in observed if inv['role']=='worker']
            profiles={'/'.join(inv['observedModels']+inv['observedEfforts']) for inv in workers}
            if workers and len(profiles)==1:
                row['workerProfile']=next(iter(profiles));row['workerEnvelopeUnionSeconds']=union_seconds([pair for inv in workers for pair in intervals(inv['occurredAt'])])
        rows.append(row)
    metrics=['responseEnvelopeThroughNativeCloseSeconds','responseEnvelopeUnionSeconds','summedInvocationEnvelopeSeconds','dispatchToCloseSeconds','gapCappedUnionSeconds3600','gapCappedUnionSeconds28800','gapCappedUnionSeconds86400']
    results={m:association(rows,m,a.seed,a.bootstrap) for m in metrics}
    creation=[dict(r,H=r['creationH']) for r in rows]
    # A documentary wait-to-readiness scenario is not a verified execution pause.
    wait_case=next((r for r in creation if r['itemId']=='FS-GG/FS.GG.Rendering#1261'),None)
    pause_scenario=None
    if wait_case is not None:
        record=histories[native[wait_case['itemId']]['node_id']]
        comments={c['id']:c for c in h.connection(record['comments'])}
        start=comments.get('IC_kwDOS6fcy88AAAABQN-Ywg');end_wait=comments.get('IC_kwDOS6fcy88AAAABQOjntQ')
        if start and end_wait and 'parked' in start['body'] and 'status=Ready' in end_wait['body']:
            seconds=(h.instant(end_wait['createdAt'])-h.instant(start['createdAt'])).total_seconds()
            adjusted=[dict(r,calendarClosedSeconds=r['calendarClosedSeconds']-seconds) if r is wait_case else r for r in creation]
            pause_scenario={'kind':'declared supplier park to readiness, not execution stop/resume','seconds':seconds,'rawCaseCalendarSeconds':wait_case['calendarClosedSeconds'],'adjustedCaseCalendarSeconds':wait_case['calendarClosedSeconds']-seconds,'association':association(adjusted,'calendarClosedSeconds',a.seed,a.bootstrap)}
    results['creationH_calendarClosedSeconds']=association(creation,'calendarClosedSeconds',a.seed,a.bootstrap)
    results['currentH_responseEnvelopeUnionSeconds']=association([dict(r,H=r['currentH']) for r in rows],'responseEnvelopeUnionSeconds',a.seed,a.bootstrap)
    profiles=sorted({r.get('workerProfile') for r in rows if r.get('workerProfile')})
    for metric in metrics:
        selected=[r for r in rows if r.get(metric) is not None]
        values=np.log1p([r[metric] for r in selected]);med=float(np.median(values)) if len(values) else 0;mad=float(stats.median_abs_deviation(values)) if len(values) else 0
        for row,value in zip(selected,values):row[metric+'MADFlag']=bool(mad>0 and abs(value-med)>3*mad)
    population={}
    for kind in ('issue','pull_request'):
        for status in ('open','closed'):
            selected=[r for r in census if r['kind']==kind and r['state']==status]
            population[kind+':'+status]=describe([(h.instant(r['closed_at']) if r['closed_at'] else cutoff).timestamp()-h.instant(r['created_at']).timestamp() for r in selected])
    outcome_strata={}
    for key,selected in [('completed',[r for r in census if r['kind']=='issue' and r['state_reason']=='completed' and r['closed_at']]),('not_planned',[r for r in census if r['kind']=='issue' and r['state_reason']=='not_planned' and r['closed_at']]),('duplicate',[r for r in census if r['kind']=='issue' and r['state_reason']=='duplicate' and r['closed_at']]),('merged_pr',[r for r in census if r['kind']=='pull_request' and r['pullRequest'].get('merged_at')]),('unmerged_closed_pr',[r for r in census if r['kind']=='pull_request' and r['state']=='closed' and not r['pullRequest'].get('merged_at')])]:
        outcome_strata[key]=describe([(h.instant(r['closed_at'])-h.instant(r['created_at'])).total_seconds() for r in selected])
    report={'schema':'fsgg.learn-history-statistics/1','versions':{'python':sys.version.split()[0],'numpy':np.__version__,'scipy':scipy.__version__},'seed':a.seed,
       'population':population,'populationOutcomeStrata':outcome_strata,'stratifiedRuntimeTailAudit':{field:stratified_tails(rows,'responseEnvelopeUnionSeconds',field) for field in ('repository','era','workerProfile')},'cohort':{'qualifiedHistories':len(rows),'withDispatchH':sum(r['H'] is not None for r in rows),'withCreationH':sum(r['creationH'] is not None for r in rows),'withResponseTimelines':len(by_item),'invocations':len(seen),'responses':response['responses'],'states':dict(collections.Counter(r['state']+':'+str(r['stateReason']) for r in rows)),'unknowns':dict(unknown),'creationVsDispatchHChanges':sum(r['H']!=r['creationH'] for r in rows if r['H'] is not None),'dispatchVsCurrentHChanges':sum(r['H']!=r['currentH'] for r in rows if r['H'] is not None),'responsesAfterClose':sum(r.get('responsesAfterNativeClose') or 0 for r in rows)},
       'documentaryWaitScenario':pause_scenario,'categoryEnvelopeSummaries':{category:describe([r['responseEnvelopeUnionSeconds'] for r in rows if r.get('HCategory')==category and 'responseEnvelopeUnionSeconds' in r]) for category in ('Low','Medium','High','Unknown')},'associations':results,'workerProfiles':{p:association([r for r in rows if r.get('workerProfile')==p],'workerEnvelopeUnionSeconds',a.seed,a.bootstrap) for p in profiles},'profileInteraction':profile_interaction(rows,a.seed,a.bootstrap),
       'limitations':['No verified active execution intervals: response envelopes and gap caps are proxies, not active time.','Native closed cohort includes unsuccessful closure; native completion is not harmonized quality acceptance.','Timestamp data are partial telemetry, with unknown terminal coverage; no assertion full worked-time-to-done.','No automatic outlier exclusion, no measured pause adjustment without supported intervals.','Bootstrap conditions on selected cohort and observed parent components; missing dependencies can understate uncertainty.','First observed dispatch may follow actual work; creation, boundary and current bodies are separate.','No causal effects or cross-profile latent invariance established.']}
    for path in [a.first_dispatches,a.response_times,a.scorer,Path(__file__)]:sources.append({'path':str(path),'sha256':h.digest(path.read_bytes())})
    report['sourceManifestDigest']=h.digest(h.canonical(sources));a.output.mkdir(mode=0o700,parents=True)
    for name,value in [('aggregate.json',report),('PRIVATE-rows.json',rows),('PRIVATE-source-manifest.json',sources)]:
        (a.output/name).write_text(json.dumps(value,indent=2,allow_nan=False)+'\n')
    print(json.dumps({'rows':len(rows),'responseItems':len(by_item),'output':str(a.output)}))

if __name__=='__main__':main()
