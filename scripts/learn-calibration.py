#!/usr/bin/env python3
"""Aggregate private calibration rows; never print rows, identities, or raw tokens.
Input: {deliveries:[{hardness,observedTokens:null|positive,reviewRounds:[positive...],blockingFindings:null|nonnegative}]}.
Rows are unique original Deliveries, not PRs. Missing measurements remain null.
"""
import argparse
import json
import math
import statistics

def correlation(x,y):
 if len(x)<3 or len(set(x))<2 or len(set(y))<2:return None
 a,b=statistics.mean(x),statistics.mean(y)
 return sum((v-a)*(w-b) for v,w in zip(x,y))/math.sqrt(sum((v-a)**2 for v in x)*sum((w-b)**2 for w in y))

def aggregate(data):
 rows=data['deliveries']
 if not rows:raise ValueError('empty cohort')
 for r in rows:
  if type(r['hardness']) is not int or not 0<=r['hardness']<=24:raise ValueError('invalid hardness')
  if r['observedTokens'] is not None and (type(r['observedTokens']) is not int or r['observedTokens']<=0):raise ValueError('invalid tokens')
  if any(type(n) is not int or n<1 for n in r['reviewRounds']):raise ValueError('invalid review rounds')
  if r['blockingFindings'] is not None and (type(r['blockingFindings']) is not int or r['blockingFindings']<0):raise ValueError('invalid blocking count')
 review=[(r['hardness'],n) for r in rows for n in r['reviewRounds']]
 unique=[(r['hardness'],statistics.mean(r['reviewRounds'])) for r in rows if r['reviewRounds']]
 tokens=[(r['hardness'],math.log(r['observedTokens'])) for r in rows if r['observedTokens'] is not None]
 blocking=[(r['hardness'],r['blockingFindings']) for r in rows if r['blockingFindings'] is not None]
 def corr(pairs):return correlation([p[0] for p in pairs],[p[1] for p in pairs])
 return dict(uniqueDeliveries=len(rows),reviewExamples=len(review),reviewRoundSum=sum(n for _,n in review),
  hardnessRange=[min(r['hardness'] for r in rows),max(r['hardness'] for r in rows)],reviewPearson=corr(review),
  deliveryMeanReviewPearson=corr(unique),tokenJoined=len(tokens),logTokenPearson=corr(tokens),
  sampleLogTokenSd=statistics.stdev(n for _,n in tokens) if len(tokens)>1 else None,
  knownBlockingCounts=len(blocking),blockingPearson=corr(blocking),inference='descriptive-only; selected small cohort and partial usage')
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--input',required=True);a=p.parse_args()
 print(json.dumps(aggregate(json.load(open(a.input))),sort_keys=True,indent=2))
