#!/usr/bin/env python3
"""Aggregate a sealed fresh-judge pilot. Never export packets or individual ratings.
The frozen protocol defines the comparisons; this script does not select a winning judge.
"""
import argparse
import collections
import datetime as dt
import hashlib
import importlib.util
import itertools
import math
import json
import os
from pathlib import Path
import sys
import numpy as np
import scipy
from scipy import stats

CATEGORIES=['minimal','bounded','complex','research']
PROFILES=['sol-medium','sol-high','astra-medium','astra-high']
ENDPOINTS=['calendarCompletedSeconds','calendarClosedSeconds','responseEnvelopeThroughNativeCloseSeconds','responseEnvelopeUnionSeconds','summedInvocationEnvelopeSeconds','gapCappedUnionSeconds3600','gapCappedUnionSeconds28800','gapCappedUnionSeconds86400']
SEED=5277
REPLICATES=1000

def digest(data):return hashlib.sha256(data).hexdigest()
def module(path):
    spec=importlib.util.spec_from_file_location('frozen_pilot',path);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m

def coefficient(x,y,kind):
    if len(x)<3 or len(set(x))<2 or len(set(y))<2:return None
    value=(stats.spearmanr(x,y) if kind=='spearman' else stats.kendalltau(x,y)).statistic
    return float(value) if np.isfinite(value) else None

def agreement(left,right,levels):
    require(len(left)==len(right),'unequal paired arrays')
    n=len(left);k=len(levels);matrix=np.zeros((k,k),dtype=int)
    for a,b in zip(left,right):matrix[levels.index(a),levels.index(b)]+=1
    result={'n':n,'levels':levels,'confusion':matrix.tolist(),'exactAgreement':float(np.trace(matrix)/n) if n else None,'unweightedKappa':None,'linearWeightedKappa':None}
    if not n:return result
    observed=matrix/n;expected=np.outer(observed.sum(axis=1),observed.sum(axis=0));distance=np.abs(np.arange(k)[:,None]-np.arange(k)[None,:])/(k-1)
    chance=float(np.trace(expected));result['unweightedKappa']=float((np.trace(observed)-chance)/(1-chance)) if chance<1 else None
    denom=float(np.sum(expected*distance));result['linearWeightedKappa']=float(1-np.sum(observed*distance)/denom) if denom>0 else None
    return result

def require(ok,message):
    if not ok:raise ValueError(message)

def cluster_interval(values,clusters):
    groups=sorted(set(clusters));by={g:np.array([v for v,c in zip(values,clusters) if c==g]) for g in groups}
    result={'n':len(values),'clusters':len(groups),'mean':float(np.mean(values)) if values else None,'median':float(np.median(values)) if values else None,'interval95':None,'bootstrapReplicates':REPLICATES,'seed':SEED,'resamplingUnit':'observed historical parent component','interpretation':'descriptive observed-cohort interval; few clusters and nonrandom historical selection limit inference'}
    if len(groups)<4:
        return dict(result,status='interval withheld with fewer than four observed components; threshold is a reporting guard, not proof of reliable inference')
    rng=np.random.default_rng(SEED);sample=[float(np.mean(np.concatenate([by[g] for g in rng.choice(groups,len(groups),replace=True)]))) for _ in range(REPLICATES)]
    result.update(interval95=np.quantile(sample,[.025,.975]).tolist(),degenerateResamples=sum(x==sample[0] for x in sample) if len(set(sample))==1 else 0,equalClusterMean=float(np.mean([np.mean(v) for v in by.values()])))
    return result

def ordinal(rating,exclude_research=False):
    category=rating['demand']['category']
    return CATEGORIES.index(category) if category in CATEGORIES and not(exclude_research and category=='research') else None

def metric_pairs(ratings,a,b,exclude_research=False):
    pairs=[]
    for item,by in ratings.items():
        if a not in by or b not in by:continue
        x,y=ordinal(by[a],exclude_research),ordinal(by[b],exclude_research)
        if x is not None and y is not None:pairs.append((item,x,y))
    x=[p[1] for p in pairs];y=[p[2] for p in pairs]
    result=agreement(x,y,list(range(3 if exclude_research else 4)))
    result.update(spearman=coefficient(x,y,'spearman'),kendall=coefficient(x,y,'kendall'),meanAbsoluteDifference=float(np.mean(np.abs(np.array(y)-x))) if pairs else None,meanSignedRightMinusLeft=float(np.mean(np.array(y)-x)) if pairs else None)
    return result

def pairwise(ratings):
    result=[]
    for a,b in itertools.combinations(PROFILES,2):
        paired=[v for v in ratings.values() if a in v and b in v]
        nominal=agreement([v[a]['demand']['category'] for v in paired],[v[b]['demand']['category'] for v in paired],CATEGORIES+['unknown'])
        nominal.pop('linearWeightedKappa')
        result.append({'left':a,'right':b,'nominalIncludingUnknown':nominal,'ordinalHypothesis':metric_pairs(ratings,a,b),'researchExcludedRanks':metric_pairs(ratings,a,b,True)})
    return result

def component_agreement(ratings):
    output=[]
    fields={'workVolume':lambda r:r['work_volume']['category'],'safety':lambda r:r['safety']['consequence'],**{f:(lambda r,f=f:r['facets'][f]) for f in ['decomposition','coupling','reasoning','novelty','validation']}}
    for a,b in itertools.combinations(PROFILES,2):
        paired=[v for v in ratings.values() if a in v and b in v];components={}
        for field,get in fields.items():
            known=[v for v in paired if get(v[a])!='unknown' and get(v[b])!='unknown'];levels=['small','moderate','large'] if field=='workVolume' else ['low','medium','high'];components[field]=agreement([get(v[a]) for v in known],[get(v[b]) for v in known],levels);components[field]['pairedMissingOrUnknown']=len(paired)-len(known)
        times=[v for v in paired if v[a]['worked_time']['p50_minutes'] is not None and v[b]['worked_time']['p50_minutes'] is not None];x=[v[a]['worked_time']['p50_minutes'] for v in times];y=[v[b]['worked_time']['p50_minutes'] for v in times]
        components['timeMedian']={'n':len(times),'spearman':coefficient(x,y,'spearman'),'kendall':coefficient(x,y,'kendall'),'meanAbsoluteLog1pDifference':float(np.mean(np.abs(np.log1p(y)-np.log1p(x)))) if times else None};output.append({'left':a,'right':b,'components':components})
    return output

def primary_contrasts(ratings,clusters):
    result={}
    for name,terms in [('sol_high_minus_medium',{'sol-high':1,'sol-medium':-1}),('astra_high_minus_medium',{'astra-high':1,'astra-medium':-1}),('astra_minus_sol_averaged_effort',{'astra-high':.5,'astra-medium':.5,'sol-high':-.5,'sol-medium':-.5})]:
        rows=[]
        for item,by in ratings.items():
            if all(p in by and ordinal(by[p]) is not None for p in terms):rows.append((item,sum(weight*ordinal(by[p]) for p,weight in terms.items())))
        out=cluster_interval([v for _,v in rows],[clusters[i] for i,_ in rows]);ci=out['interval95'];out['plannedPrimaryItems']=len(clusters);out['missingOrUnknownPairedItems']=len(clusters)-len(rows);out['hypotheticalAllItemBoundIfUnobservedCategoriesLieInZeroToThree']=[(sum(v for _,v in rows)-3*(len(clusters)-len(rows)))/len(clusters),(sum(v for _,v in rows)+3*(len(clusters)-len(rows)))/len(clusters)];out['proposedEquivalenceMargin']=.5;out['conditionalObservedMeanInsideProposedMargin']=bool(ci and ci[0]>-.5 and ci[1]<.5) if ci else None
        result[name]=out
    return result

def prediction(rating,name):
    if name=='reasoning':return ordinal(rating)
    if name=='workVolume':
        v=rating['work_volume']['category'];return ['small','moderate','large'].index(v) if v!='unknown' else None
    if name=='logWorkedMinutes':
        v=rating['worked_time']['p50_minutes'];return float(np.log1p(v)) if v is not None else None
    raise ValueError(name)

def association(rows,xname,yname):
    rows=[r for r in rows if r.get(xname) is not None and r.get(yname) is not None];x=[r[xname] for r in rows];y=[r[yname] for r in rows]
    return {'n':len(rows),'clusters':len({r['cluster'] for r in rows}),'xUnique':len(set(x)),'yUnique':len(set(y)),'spearman':coefficient(x,y,'spearman'),'kendall':coefficient(x,y,'kendall')}

def historical_rows(mapping,history):
    indexed={r['itemId']:r for r in history};require(len(indexed)==len(history),'duplicate historical row')
    result={}
    for m in mapping:
        row=dict(indexed.get(m['historicalItem'],{}));row['cluster']=row.get('cluster') or m.get('parent') or m['historicalItem'];row['state']=m['state'];row['stateReason']=m['stateReason'];row['creationBoundary']=m['createdAt']
        if m['closedAt']:
            seconds=(dt.datetime.fromisoformat(m['closedAt'].replace('Z','+00:00'))-dt.datetime.fromisoformat(m['createdAt'].replace('Z','+00:00'))).total_seconds();row['calendarClosedSeconds']=seconds
            row['calendarCompletedSeconds']=seconds if m['stateReason']=='completed' else None
        else:row['calendarClosedSeconds']=None;row['calendarCompletedSeconds']=None
        result[m['itemId']]=row
    return result

def rank_cluster_interval(rows,xname,yname):
    groups=sorted({r['cluster'] for r in rows});out={'clusters':len(groups),'replicates':REPLICATES,'seed':SEED,'interval95':None,'scope':'exploratory observed-cohort cluster resampling, not independent validation'}
    if len(groups)<4:return dict(out,status='interval withheld below four components')
    by={g:[r for r in rows if r['cluster']==g] for g in groups};rng=np.random.default_rng(SEED);values=[]
    for _ in range(REPLICATES):
        sample=[r for g in rng.choice(groups,len(groups),replace=True) for r in by[g]];v=coefficient([r[xname] for r in sample],[r[yname] for r in sample],'spearman')
        if v is not None:values.append(v)
    out['undefinedResamples']=REPLICATES-len(values)
    if values:out['interval95']=np.quantile(values,[.025,.975]).tolist()
    return out

def duration_analysis(ratings,history):
    all_results=[]
    for profile in PROFILES:
        for predictor in ['reasoning','workVolume','logWorkedMinutes']:
            for endpoint in ENDPOINTS:
                rows=[]
                for item,by in ratings.items():
                    if profile not in by:continue
                    h=history[item];rows.append(dict(h,newPredictor=prediction(by[profile],predictor),oldH=h.get('creationH'),logTextWords=float(np.log1p(h['creationTextWords'])) if h.get('creationTextWords') is not None else None))
                for completed in [False,True]:
                    subset=[r for r in rows if not completed or r['stateReason']=='completed'];paired=[r for r in subset if all(r.get(k) is not None for k in ['newPredictor','oldH','logTextWords',endpoint])]
                    results={k:association(paired,k,endpoint) for k in ['newPredictor','oldH','logTextWords']}
                    if predictor=='logWorkedMinutes' and endpoint in ['calendarCompletedSeconds','responseEnvelopeUnionSeconds','responseEnvelopeThroughNativeCloseSeconds'] and not completed:results['newPredictor']['exploratoryClusterInterval']=rank_cluster_interval(paired,'newPredictor',endpoint)
                    strata={p:association([r for r in paired if r.get('workerProfile')==p],'newPredictor',endpoint) for p in sorted({r.get('workerProfile') for r in paired if r.get('workerProfile')})}
                    all_results.append({'judgeProfile':profile,'predictor':predictor,'endpoint':endpoint,'restriction':'native_completed_only' if completed else 'all_available_outcomes','eligibleRatings':len(subset),'newPredictorCoverage':sum(r['newPredictor'] is not None and r.get(endpoint) is not None for r in subset),'matchedBaselineN':len(paired),'sameRowsComparison':results,'historicalExecutorStrata':strata})
    return all_results

def ridge(train,test,features,endpoint):
    y=np.log1p([r[endpoint] for r in train])
    if features=='median':return np.repeat(np.median(y),len(test))
    if features=='mean':return np.repeat(np.mean(y),len(test))
    X=np.array([[r[k] for k in features] for r in train],float);T=np.array([[r[k] for k in features] for r in test],float);mu=X.mean(axis=0);sd=X.std(axis=0);sd[sd==0]=1;X=(X-mu)/sd;T=(T-mu)/sd
    beta=np.linalg.solve(X.T@X+10*np.eye(X.shape[1]),X.T@(y-y.mean()));return y.mean()+T@beta

def grouped_prediction(rows,endpoint):
    features=['reasoning','workVolume','logWorkedMinutes','oldH','logTextWords'];rows=[r for r in rows if all(r.get(k) is not None for k in features+[endpoint])];groups=sorted({r['cluster'] for r in rows})
    result={'n':len(rows),'clusters':len(groups),'endpoint':endpoint,'ridgeAlpha':10,'foldLocalStandardization':True,'models':{},'interpretation':'exploratory reuse, not an untouched validation set'}
    if len(groups)<4 or len(rows)-max((sum(r['cluster']==g for r in rows) for g in groups),default=0)<4:return dict(result,status='unsupported parent-separated training support')
    models={'mean':'mean','median':'median',**{k:[k] for k in features},'reasoning_plus_volume':['reasoning','workVolume']}
    for name,fs in models.items():
        errors=[];folds=[]
        for g in groups:
            train=[r for r in rows if r['cluster']!=g];test=[r for r in rows if r['cluster']==g];err=np.abs(ridge(train,test,fs,endpoint)-np.log1p([r[endpoint] for r in test]));errors.extend(err);folds.append({'trainN':len(train),'testN':len(test),'logMAE':float(np.mean(err))})
        result['models'][name]={'pooledLogMAE':float(np.mean(errors)),'equalClusterLogMAE':float(np.mean([f['logMAE'] for f in folds])),'folds':folds}
    dates=sorted(r['creationBoundary'] for r in rows);cut=dates[len(dates)//2];trainGroups=[];testGroups=[];purged=[]
    for g in groups:
        times=[r['creationBoundary'] for r in rows if r['cluster']==g];(trainGroups if max(times)<cut else testGroups if min(times)>=cut else purged).append(g)
    train=[r for r in rows if r['cluster'] in trainGroups];test=[r for r in rows if r['cluster'] in testGroups]
    temporal={'trainGroups':len(trainGroups),'testGroups':len(testGroups),'purgedGroups':len(purged),'trainN':len(train),'testN':len(test),'models':{}}
    if len(trainGroups)>=3 and len(testGroups)>=2 and len(train)>=10 and len(test)>=5:
        temporal['models']={name:{'logMAE':float(np.mean(np.abs(ridge(train,test,fs,endpoint)-np.log1p([r[endpoint] for r in test]))))} for name,fs in models.items()}
    else:temporal['status']='unsupported chronology and parent separation; no score'
    result['temporal']=temporal;return result

def controls_analysis(all_ratings):
    output=[]
    for p in PROFILES:
        for a,b,kind in [('trial-051','trial-052','negated exclusions'),('trial-051','trial-053','quoted irrelevant history'),('trial-054','trial-055','embedded instruction'),('trial-051','trial-054','baseline separation'),('trial-001','trial-056','fresh repeat one'),('trial-002','trial-057','fresh repeat two')]:
            left=all_ratings.get(a,{}).get(p);right=all_ratings.get(b,{}).get(p);record={'profile':p,'control':kind,'pairedValid':bool(left and right)}
            if left and right:
                x,y=ordinal(left),ordinal(right);record.update(categoryEqual=left['demand']['category']==right['demand']['category'],workVolumeEqual=left['work_volume']['category']==right['work_volume']['category'],timeLogDifference=float(np.log1p(right['worked_time']['p50_minutes'])-np.log1p(left['worked_time']['p50_minutes'])) if left['worked_time']['p50_minutes'] is not None and right['worked_time']['p50_minutes'] is not None else None,rightMinusLeft=y-x if x is not None and y is not None else None)
                if not kind.startswith('fresh repeat'):record.update(leftCategory=left['demand']['category'],rightCategory=right['demand']['category'],leftWorkVolume=left['work_volume']['category'],rightWorkVolume=right['work_volume']['category'],leftMedianMinutes=left['worked_time']['p50_minutes'],rightMedianMinutes=right['worked_time']['p50_minutes'])
            output.append(record)
    synthetic=[r for r in output if not r['control'].startswith('fresh repeat')];repeats=[]
    for p in PROFILES:
        rows=[r for r in output if r['profile']==p and r['control'].startswith('fresh repeat')];valid=[r for r in rows if r['pairedValid']];known=[r['rightMinusLeft'] for r in valid if r['rightMinusLeft'] is not None]
        repeats.append({'profile':p,'plannedPairs':2,'validPairs':len(valid),'equalCategories':sum(r['categoryEqual'] for r in valid),'equalWorkVolumes':sum(r['workVolumeEqual'] for r in valid),'knownTimePairs':sum(r['timeLogDifference'] is not None for r in valid),'meanAbsoluteTimeLogDifference':float(np.mean([abs(r['timeLogDifference']) for r in valid if r['timeLogDifference'] is not None])) if any(r['timeLogDifference'] is not None for r in valid) else None,'knownOrdinalPairs':len(known),'meanAbsoluteCategoryDifference':float(np.mean(np.abs(known))) if known else None})
    return {'synthetic':synthetic,'repeatsAggregated':repeats}

def validate_content_fields(value,packet):
    """Post hoc content-only check. Does not mutate bytes or certify interface compliance."""
    require(value['schema']=='fsgg.semantic-hardness/1' and value['item_id']==packet['item_id'],'content input identity')
    demand=value['demand'];require(set(demand)=={'category','confidence','basis','evidence'},'content demand fields')
    require(demand['category'] in CATEGORIES+['unknown'] and demand['confidence'] in ['low','medium','high'],'content category')
    require(isinstance(demand['basis'],str) and bool(demand['basis']),'content basis')
    lines={r['line']:r['text'] for r in packet['task_lines']};require(isinstance(demand['evidence'],list) and 1<=len(demand['evidence'])<=3,'content evidence count')
    for e in demand['evidence']:
        require(set(e)=={'line_start','line_end','quote'},'content evidence fields');a,b=e['line_start'],e['line_end'];require(type(a)is int and type(b)is int and 1<=a<=b<=len(lines),'content evidence bounds');require(isinstance(e['quote'],str) and bool(e['quote']) and e['quote'] in '\n'.join(lines[i] for i in range(a,b+1)),'content quote mismatch')
    volume=value['work_volume'];require(set(volume)=={'category','basis'} and volume['category'] in ['small','moderate','large','unknown'] and isinstance(volume['basis'],str) and bool(volume['basis']),'content volume')
    t=value['worked_time'];require(set(t)=={'reference','p10_minutes','p50_minutes','p90_minutes','confidence'} and t['reference']=='reference-agent-work/1' and t['confidence'] in ['low','medium','high'],'content time fields')
    v=[t[k] for k in ['p10_minutes','p50_minutes','p90_minutes']];require(all(z is None for z in v) or all(type(z) in [int,float] and math.isfinite(z) and z>=0 for z in v) and v==sorted(v),'content time values')
    return True

def audit_launch(row,ledger,controller):
    require([e['event'] for e in ledger]==['reserved','accepted_launch','collected'],'invalid launch provenance sequence')
    reserved,accepted,end=ledger;request=reserved['request'];actual=accepted['acceptedRequest']
    require(digest(controller.canonical(request))==reserved['requestSha256'],'reservation hash mismatch')
    require(request['model']==row['requestedModel'] and request['reasoning_effort']==row['requestedEffort'] and request['fork_turns']=='none','planned profile mismatch')
    require(actual['model']==request['model'] and actual['reasoning_effort']==request['reasoning_effort'] and actual['fork_turns']=='none','actual judge profile mismatch')
    require(accepted['canonicalAgent'].endswith('/'+row['taskName']) and end['canonicalAgent']==accepted['canonicalAgent'],'actual agent identity mismatch')
    changed=actual!=request
    require(not changed or accepted.get('protocolDeviation'),'unrecorded actual request mismatch')
    require(changed or not accepted.get('protocolDeviation'),'deviation without changed request')
    if 'acceptedRequestSha256' in accepted:require(digest(controller.canonical(actual))==accepted['acceptedRequestSha256'],'actual request hash mismatch')
    return changed

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--pilot',type=Path,required=True);p.add_argument('--history-rows',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();os.umask(0o077)
    require(not a.output.exists(),'preserve previous analysis directory');controller=module(a.pilot/'CONTROLLER.py');seal=controller.verify_seal(a.pilot);schedule=json.loads((a.pilot/'schedule.json').read_text());events=controller.events(a.pilot);mapping=json.loads((a.pilot/'PRIVATE-cohort.json').read_text());history=historical_rows(mapping,json.loads(a.history_rows.read_text()));ratings={};counts=collections.Counter();profileCounts={p:collections.Counter() for p in PROFILES};sources=[{'path':str(a.history_rows),'sha256':digest(a.history_rows.read_bytes())},{'path':str(Path(__file__)),'sha256':digest(Path(__file__).read_bytes())},{'path':str(a.pilot/'launch-events.jsonl'),'sha256':digest((a.pilot/'launch-events.jsonl').read_bytes())}]
    failures=collections.Counter();actual_agents=[];deviations=0;cellStatus={};contentOnly={};contentFailures=collections.Counter();boundedFields=collections.Counter()
    for row in schedule:
        ledger=[e for e in events if e['sequence']==row['sequence']];terminal=[e for e in ledger if e['event'] in ['collected','launch_error']];require(len(terminal)==1,'every scheduled cell must have one terminal record; preserve pending calls');end=terminal[0];status=end['event'];valid=False
        if status=='collected':
            require(ledger[0]['request']==controller.launch_request(a.pilot,row),'reserved payload differs from frozen scheduled payload');deviated=audit_launch(row,ledger,controller);deviations+=int(deviated);actual_agents.append(ledger[1]['canonicalAgent']);path=Path(row['outputPath'])
            if end['rawOutputSha256'] is not None:
                raw=path.read_bytes();require(digest(raw)==end['rawOutputSha256'],'output changed after collection');sources.append({'path':str(path),'sha256':digest(raw)})
            valid=end['validation']['valid'] and not deviated
            if not valid:failures[(row['kind'],row['profileId'],'controller_input_binding' if deviated else end['validation'].get('error','unknown failure'))]+=1
            if not valid and not deviated and end['validation'].get('error')=='invalid bounded text':
                candidate=controller.decode_response(raw)
                for field,value,limit in [('demand.basis',candidate['demand']['basis'],280),('work_volume.basis',candidate['work_volume']['basis'],180),('safety.basis',candidate['safety']['basis'],180)]+[('evidence.quote',e['quote'],180) for e in candidate['demand']['evidence']]+[('missing_context',v,140) for v in candidate['conditions']['missing_context']]:
                    if not isinstance(value,str) or not 0<len(value)<=limit:boundedFields[field]+=1
                try:validate_content_fields(candidate,json.loads(Path(row['packetPath']).read_text()));contentOnly.setdefault(row['itemId'],{})[row['profileId']]=candidate
                except (ValueError,TypeError,KeyError) as error:contentFailures[str(error)]+=1
            if valid:
                value=controller.decode_response(raw);controller.validate(value,json.loads(Path(row['packetPath']).read_text()));ratings.setdefault(row['itemId'],{})[row['profileId']]=value
        label='valid' if valid else 'invalid_or_missing';cellStatus.setdefault(row['itemId'],{})[row['profileId']]='valid' if valid else 'controller_input_binding' if status=='collected' and deviated else 'invalid_or_missing';counts[(row['kind'],label)]+=1
        if row['kind']=='primary':profileCounts[row['profileId']][label]+=1
    require(len(actual_agents)==len(set(actual_agents)),'agent reused across cells')
    require(len(schedule)==228 and sum(r['kind']=='primary' for r in schedule)==200,'frozen denominator mismatch')
    primary={i:v for i,v in ratings.items() if int(i[-3:])<=50};clusters={i:r['cluster'] for i,r in history.items()};support={}
    for p in PROFILES:
        valid=[by[p] for by in primary.values() if p in by];support[p]={'scheduled':50,**dict(profileCounts[p]),'demandCategories':dict(collections.Counter(r['demand']['category'] for r in valid)),'workVolumeCategories':dict(collections.Counter(r['work_volume']['category'] for r in valid)),'timeKnown':sum(r['worked_time']['p50_minutes'] is not None for r in valid),'facets':{f:dict(collections.Counter(r['facets'][f] for r in valid)) for f in ['decomposition','coupling','reasoning','novelty','validation']}}
    for p in PROFILES:
        support[p]['validByNativeOutcome']=dict(collections.Counter(str(history[i]['stateReason']) for i,by in primary.items() if p in by));times=[by[p]['worked_time']['p50_minutes'] for by in primary.values() if p in by and by[p]['worked_time']['p50_minutes'] is not None];support[p]['subjectiveMedianMinutesQuantiles']=np.quantile(times,[0,.25,.5,.75,1]).tolist() if times else None
    contrasts=primary_contrasts(primary,clusters)
    for name,profiles in [('sol_high_minus_medium',['sol-high','sol-medium']),('astra_high_minus_medium',['astra-high','astra-medium']),('astra_minus_sol_averaged_effort',PROFILES)]:
        coverage=collections.Counter()
        for item in history:
            states=[cellStatus[item][p] for p in profiles]
            label='controller_input_binding' if 'controller_input_binding' in states else 'invalid_or_missing' if any(s!='valid' for s in states) else 'unknown_category' if any(ordinal(primary[item][p]) is None for p in profiles) else 'included_known'
            coverage[label]+=1
        contrasts[name]['mutuallyExclusivePairCoverage']=dict(coverage);contrasts[name]['bootstrapConditionalOnObservedKnownPairs']=True;contrasts[name]['populationOrExecutionInvarianceEstablished']=False
    disagreement=collections.Counter()
    for by in primary.values():
        known=[ordinal(r) for r in by.values() if ordinal(r) is not None]
        if len(known)>=2:disagreement[(len(known),max(known)-min(known))]+=1
    cv={}
    for p in PROFILES:
        rows=[dict(history[i],reasoning=prediction(by[p],'reasoning'),workVolume=prediction(by[p],'workVolume'),logWorkedMinutes=prediction(by[p],'logWorkedMinutes'),oldH=history[i].get('creationH'),logTextWords=float(np.log1p(history[i]['creationTextWords'])) if history[i].get('creationTextWords') is not None else None) for i,by in primary.items() if p in by]
        cv[p]={e:grouped_prediction(rows,e) for e in ['calendarCompletedSeconds','responseEnvelopeUnionSeconds','responseEnvelopeThroughNativeCloseSeconds']}
    posthoc={i:dict(by) for i,by in primary.items()}
    for i,by in contentOnly.items():posthoc.setdefault(i,{}).update(by)
    missingness=[]
    for profile in PROFILES:
        for state in ['valid','invalid_or_missing','controller_input_binding']:
            hs=[history[i] for i in history if cellStatus[i][profile]==state];vals={k:[r[k] for r in hs if r.get(k) is not None] for k in ['creationH','creationTextWords']}
            missingness.append({'profile':profile,'pipelineStatus':state,'n':len(hs),'creationHMedian':float(np.median(vals['creationH'])) if vals['creationH'] else None,'creationHKnown':len(vals['creationH']),'creationTextWordsMedian':float(np.median(vals['creationTextWords'])) if vals['creationTextWords'] else None,'creationTextWordsKnown':len(vals['creationTextWords']),'creationMonthCounts':dict(collections.Counter(r['creationBoundary'][:7] for r in hs)),'nativeOutcomeCounts':dict(collections.Counter(str(r['stateReason']) for r in hs))})
    for row in missingness:
        if row['n']<3:
            for field in ['creationHMedian','creationTextWordsMedian','creationMonthCounts','nativeOutcomeCounts']:row[field]=None
            row['smallCellAttributes']='suppressed below three cells; pipeline counts retained'
    posthocResult={'status':'POST HOC content-only sensitivity; original strict failures remain failures','addedUnchangedContentFields':sum(map(len,contentOnly.values())),'contentCheckFailures':dict(contentFailures),'boundedTextFailureFields':dict(boundedFields),'wrongInputExcluded':True,'validContentCounts':{p:sum(p in by for by in posthoc.values()) for p in PROFILES},'pairwiseJudgmentAgreement':pairwise(posthoc),'meanContrasts':primary_contrasts(posthoc,clusters),'historicalDurationAssociations':duration_analysis(posthoc,history),'pipelineMissingness':missingness}
    out={'schema':'fsgg.semantic-classifier-statistics/1','versions':{'python':sys.version.split()[0],'numpy':np.__version__,'scipy':scipy.__version__},'sealDigest':seal['digest'],'denominators':{'primary':200,'semantic':20,'repeat':8,'total':228},'terminalCounts':{kind:{label:counts[(kind,label)] for label in ['valid','invalid_or_missing']} for kind in ['primary','control']},'posthocContentOnly':posthocResult,'launchAudit':{'uniqueAcceptedAgents':len(actual_agents),'controllerInputBindingFailures':deviations,'providerEffective':'Unknown','toolReadTraceCoverage':'not independently established','failureCounts':[{'kind':k,'profile':p,'reason':r,'count':n} for (k,p,r),n in sorted(failures.items())]},'support':support,'historicalSupport':{'mappedPrimaryItems':len(history),'withOriginalStatistics':sum('creationH' in r for r in history.values()),'nativeOutcomeCounts':dict(collections.Counter(str(r['stateReason']) for r in history.values())),'parentSizes':sorted(collections.Counter(clusters.values()).values()),'creationVsDispatchHChanged':sum(r.get('H') is not None and r.get('creationH') is not None and r['H']!=r['creationH'] for r in history.values()),'creationVsDispatchHComparable':sum(r.get('H') is not None and r.get('creationH') is not None for r in history.values())},'pairwiseJudgmentAgreement':pairwise(primary),'componentAgreement':component_agreement(primary),'prespecifiedMeanContrasts':contrasts,'itemDisagreementSupport':[{'knownProfiles':k,'categoryRange':d,'items':n} for (k,d),n in sorted(disagreement.items())],'controlsAndRepeats':controls_analysis(ratings),'historicalDurationAssociations':duration_analysis(primary,history),'groupedPrediction':cv,'sourceManifestDigest':digest(controller.canonical(sources)),'limitations':['Judges classified snapshots; implementing-model/effort performance was not tested. Requested/accepted launch configuration is distinct from provider-effective Unknown.','Research ordering is hypothetical: nominal results and research-excluded ranks are retained. Agreement is not construct validity.','All historical time endpoints remain qualified calendar/proxy clocks; missing active-time evidence is not imputed. Historical executors and task mix differ from judges.','Historical support cohort is selected and clustered, with unequal eras; all comparisons and unsupported cells are retained. No p-value winner or causal claim.','Tool-read compliance is not established by output validation; report trace coverage separately.']}
    a.output.mkdir(mode=0o700,parents=True);(a.output/'aggregate.json').write_text(json.dumps(out,indent=2,allow_nan=False)+'\n');(a.output/'PRIVATE-source-manifest.json').write_text(json.dumps(sources,indent=2)+'\n');print(json.dumps({'output':str(a.output),'terminalCounts':out['terminalCounts']}))
if __name__=='__main__':main()
