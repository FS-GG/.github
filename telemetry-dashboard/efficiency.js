(() => {
  "use strict";
  const schema="fsgg.telemetry.process-efficiency/1", policy="efficiency-public-projection/1";
  const metrics=new Set("delivered-outcomes observed-resource work-mix avoidable-share retry-incidence retry-burden first-pass-delivery churn lead-time touch-time wait-time flow-ratio critical-path-delay cost-per-accepted analysis-burden data-health".split(" "));
  const units=new Set("tokens-input tokens-output tokens-total tokens-cached-input tokens-cache-write-input tokens-reasoning runner-seconds observed-span-seconds human-seconds seconds estimated-currency billed-currency outcomes events records ratio tokens-per-accepted currency-per-accepted".split(" "));
  const causes=new Set("product-defect requirements dependency-drift test-nondeterminism infrastructure authorization capacity-custody orchestration-handoff context-loss duplicate-process bookkeeping-lineage telemetry-loss other unknown".split(" "));
  const purposes=new Set("direct-product useful-assurance necessary-coordination process-improvement avoidable-process unknown".split(" "));
  const healthDimensions=new Set("source-age ingestion-age publication-age producer-population unresolved-lineage pending-analysis".split(" "));
  const refKinds=new Set("outcome attempt invocation operation pr ci-run ci-job release adoption usage activity complication process-review correction assessment".split(" "));
  const states=new Set("pending running partial ready failed unavailable".split(" "));
  const coverage=new Set("complete partial unknown not-applicable".split(" "));
  const axes="population usage classification lineage dependency".split(" ");
  const url=(v)=>typeof v==="string"&&/^https:\/\/github\.com\/FS-GG\/[A-Za-z0-9_.-]+\/(?:issues|pull|actions\/runs)\/[1-9][0-9]*$/.test(v);
  const object=(v)=>v!==null&&typeof v==="object"&&!Array.isArray(v);
  const exact=(v,keys)=>object(v)&&Object.keys(v).length===keys.length&&keys.every((k)=>Object.hasOwn(v,k));
  const number=(v)=>Number.isSafeInteger(v)&&v>=0;
  const timestamp=(v)=>typeof v==="string"&&v.length<=40&&/(?:Z|[+-]\d\d:\d\d)$/.test(v)&&Number.isFinite(Date.parse(v));
  const links=(v)=>Array.isArray(v)&&v.length<=16&&v.every(url);
  const fail=()=>{throw new Error("The published process efficiency data does not match its contract.");};
  function validate(feed) {
    if(!exact(feed,["schema","policyVersion","source","status","coverage","items"])||feed.schema!==schema||feed.policyVersion!==policy||!["fixtures","unavailable"].includes(feed.source)||!["partial","unavailable"].includes(feed.status)||!exact(feed.coverage,["published","unmapped","withheld","unsupported"])||!Object.values(feed.coverage).every(number)||!Array.isArray(feed.items)||feed.items.length>200||feed.coverage.published!==feed.items.length)fail();
    if(feed.coverage.unsupported>feed.coverage.withheld)fail();
    if(feed.source==="unavailable"&&(feed.status!=="unavailable"||feed.items.length))fail();
    const keys=new Set();
    feed.items.forEach((item)=>{
      if(!exact(item,["key","label","url","summary","analysisState","scope","metrics","problems","timeline","improvements"])||typeof item.key!=="string"||!/^[a-z0-9][a-z0-9-]{0,63}$/.test(item.key)||keys.has(item.key)||typeof item.label!=="string"||!item.label.length||item.label.length>120||!url(item.url)||!["native-observation","delivery-accounting-incomplete","accounting-unestablished"].includes(item.summary)||!states.has(item.analysisState)||!["native-item","provisional-delivery","unestablished"].includes(item.scope)||(item.scope==="provisional-delivery"&&item.analysisState==="ready")||!Array.isArray(item.metrics)||item.metrics.length>32)fail();
      if(item.summary!==({"native-item":"native-observation","provisional-delivery":"delivery-accounting-incomplete","unestablished":"accounting-unestablished"}[item.scope]))fail();
      keys.add(item.key);const metricKeys=new Set();
      item.metrics.forEach((m)=>{
        if(!exact(m,["key","metric","unit","calculationVersion","value","coverage","policyProfile","windowStart","windowEnd","cutoff","observedAt","eventTime","openItems","abandonedItems","excludedItems","populationItems","unmappedPopulationItems","purpose","healthDimension","sourceEvidence","unmappedEvidenceRefs","evidenceUrls"])||typeof m.key!=="string"||!/^m[0-9]+$/.test(m.key)||metricKeys.has(m.key)||!metrics.has(m.metric)||!units.has(m.unit)||m.calculationVersion!=="efficiency-calculation/1"||![null,"v2-model-overhead/2026-09-29","utel-narrow-bureaucracy/1"].includes(m.policyProfile)||!exact(m.value,["status","numerator","denominator","unknownAmount"])||!["known","partial","unknown","not-applicable"].includes(m.value.status)||![m.value.numerator,m.value.denominator,m.value.unknownAmount].every((v)=>v===null||number(v))||!exact(m.coverage,axes)||!Object.values(m.coverage).every((v)=>coverage.has(v))||![m.windowStart,m.windowEnd,m.cutoff,m.observedAt].every(timestamp)||(m.eventTime!==null&&!timestamp(m.eventTime))||![m.openItems,m.abandonedItems,m.excludedItems].every(number)||!number(m.unmappedPopulationItems)||!Array.isArray(m.populationItems)||m.populationItems.length>200||m.populationItems.some((k)=>typeof k!=="string"||!/^[a-z0-9][a-z0-9-]{0,63}$/.test(k))||!links(m.evidenceUrls))fail();
        if((m.metric==="work-mix"?!purposes.has(m.purpose):m.purpose!==null)||!number(m.unmappedEvidenceRefs)||!Array.isArray(m.sourceEvidence)||m.sourceEvidence.length>16||m.sourceEvidence.some((r)=>!exact(r,["url","kind","revision"])||!url(r.url)||!refKinds.has(r.kind)||!number(r.revision)))fail();
        if((m.metric==="data-health"?!healthDimensions.has(m.healthDimension):m.healthDimension!==null)||(m.unit==="ratio"&&["known","partial"].includes(m.value.status)&&m.value.denominator===null))fail();
        metricKeys.add(m.key);
        if((["unknown","not-applicable"].includes(m.value.status)&&(m.value.numerator!==null||m.value.denominator!==null))||(["known","partial"].includes(m.value.status)&&m.value.numerator===null)||m.value.denominator===0)fail();
      });
      if(!Array.isArray(item.problems)||item.problems.length>16||!Array.isArray(item.timeline)||item.timeline.length>32||!Array.isArray(item.improvements)||item.improvements.length>1||item.improvements.some((v)=>v!=="collect-missing-evidence"))fail();
      item.problems.forEach((p)=>{if(!exact(p,["primaryCause","necessity","epistemicStatus","evidenceUrls"])||!causes.has(p.primaryCause)||!["required","avoidable","uncertain"].includes(p.necessity)||!["observed","supported-inference","hypothesis","unknown"].includes(p.epistemicStatus)||!links(p.evidenceUrls))fail();});
      item.timeline.forEach((e)=>{if(!exact(e,["kind","at","evidenceUrls"])||!["source-delivered","native-completed","analysis-generated","metric-observed"].includes(e.kind)||!timestamp(e.at)||!links(e.evidenceUrls))fail();});
    });
  }
  let current=null,page=0;
  const $=(id)=>document.getElementById(id);
  const node=(tag,text)=>{const element=document.createElement(tag);if(text!==undefined)element.textContent=text;return element;};
  const human=(value)=>value.replaceAll("-"," ");
  function evidence(values){const cell=node("td");if(!values.length)cell.textContent="No approved public evidence link";values.forEach((value,index)=>{const link=node("a",`Evidence ${index+1}`);link.href=value;link.target="_blank";link.rel="noopener";cell.append(link,document.createTextNode(" "));});return cell;}
  function metricEvidence(refs){const cell=evidence(refs.map((r)=>r.url));refs.forEach((r)=>cell.append(node("p",`${human(r.kind)} · source revision ${r.revision}`)));return cell;}
  function table(title,headings,rows){const wrapper=node("div"),t=node("table");wrapper.className="table-scroll";t.append(node("caption",title));const head=node("thead"),line=node("tr");headings.forEach((h)=>{const cell=node("th",h);cell.scope="col";line.append(cell);});head.append(line);t.append(head);const body=node("tbody");rows.forEach((cells)=>{const tr=node("tr");cells.forEach((value)=>tr.append(typeof value==="string"?node("td",value):value));body.append(tr);});t.append(body);wrapper.append(t);return wrapper;}
  function render(feed=current){
    current=feed;
    const body=$("efficiency-items"),open=new Set([...body.querySelectorAll("details[open]")].map((d)=>d.id));body.replaceChildren();
    if(!feed||feed.status==="unavailable"){$("efficiency-health").textContent="Process efficiency unavailable: canonical measurement and assessment exports are not admitted. Runtime, cost and analysis remain unknown.";$("efficiency-page").textContent="No efficiency population available";$("efficiency-prev").disabled=true;$("efficiency-next").disabled=true;return;}
    $("efficiency-health").textContent=`Fixture preview · ${feed.coverage.published} approved items · ${feed.coverage.unmapped} unmapped · ${feed.coverage.withheld} withheld (${feed.coverage.unsupported} unsupported exact-quantity items). This is source qualification, with no installed or live acceptance. Producer/ingestion freshness remains unavailable.`;
    const search=$("efficiency-search").value.toLowerCase(),scope=$("efficiency-scope").value,view=$("efficiency-view").value;
    const filtered=feed.items.filter((item)=>item.label.toLowerCase().includes(search)&&(scope==="all"||item.scope===scope));
    const pages=Math.max(1,Math.ceil(filtered.length/10));page=Math.min(page,pages-1);
    $("efficiency-page").textContent=`Page ${page+1} of ${pages} · ${filtered.length} matching items · at most 10 per page`;
    $("efficiency-prev").disabled=page===0;$("efficiency-next").disabled=page>=pages-1;
    if(!filtered.length)body.append(node("p","No approved items match these filters. Missing evidence does not establish zero work."));
    filtered.slice(page*10,page*10+10).forEach((item)=>{
      const card=node("details"),summary=node("summary",item.label);card.id=`efficiency-${item.key}`;card.open=open.has(card.id);summary.id=`efficiency-${item.key}-toggle`;card.append(summary);
      const link=node("a","Public item evidence ↗");link.href=item.url;link.target="_blank";link.rel="noopener";card.append(link);
      card.append(node("p",`${item.summary==="native-observation"?"Native item observations; completion remains governed by the canonical item feed.":item.summary==="delivery-accounting-incomplete"?"Source delivery explanation is provisional; runtime accounting is incomplete.":"Accounting observations; delivery and native completion remain unestablished."} Analysis: ${item.analysisState}.`));
      card.append(node("p","Metrics can cover shared cohorts; repeated rows are not additive. Repository/work-type and acceptance-scope dimensions remain unavailable unless explicitly approved."));
      const selected=item.metrics.filter((m)=>view==="all"||view==="work"&&["work-mix","observed-resource","avoidable-share","analysis-burden"].includes(m.metric)||view==="retry"&&["retry-incidence","retry-burden","first-pass-delivery","churn"].includes(m.metric)||view==="health"&&m.metric==="data-health");
      if(selected.length)card.append(table("Canonical measurements (each row retains its own unit and population)",["Metric / unit","Value / denominator","Coverage / unknown amount","Population / policy","Window / observation","Evidence"],selected.map((m)=>[
        `${human(m.metric)}${m.purpose?` / ${human(m.purpose)}`:""}${m.healthDimension?` / ${human(m.healthDimension)}`:""} · ${human(m.unit)}`,
        `${m.value.status}: ${m.value.numerator===null?"Unknown":m.value.numerator}${m.value.denominator===null?" · denominator unavailable or scalar":` / ${m.value.denominator}`}`,
        `${axes.map((axis)=>`${axis}: ${m.coverage[axis]}`).join(" · ")} · unknown amount: ${m.value.unknownAmount===null?"unquantified":m.value.unknownAmount} · unmapped evidence: ${m.unmappedEvidenceRefs}`,
        `Canonical cohort: ${m.populationItems.join(', ')||'no approved labels'} · unmapped: ${m.unmappedPopulationItems} · open: ${m.openItems} · abandoned: ${m.abandonedItems} · excluded: ${m.excludedItems} · ${m.calculationVersion} · profile alias: ${m.policyProfile||"none"}`,
        `${m.windowStart} – ${m.windowEnd} · cutoff: ${m.cutoff} · observed: ${m.observedAt} · event: ${m.eventTime||"unknown"}`,metricEvidence(m.sourceEvidence)])));
      else card.append(node("p","Selected measurements unavailable. No zero cost or rate is inferred."));
      card.append(node("p",item.metrics.some((m)=>m.metric==="work-mix")?"Work mix uses explicit canonical purpose allocations. Repair can intersect purposes and is not an extra additive bucket.":"Work mix purpose allocations are unavailable for this population; repair is not an extra additive bucket."));
      if(item.problems.length)card.append(table("Problem episodes — labels are assessments, not measured cause totals",["Primary cause","Necessity","Evidence status","Evidence"],item.problems.map((p)=>[human(p.primaryCause),p.necessity,human(p.epistemicStatus),evidence(p.evidenceUrls)])));
      else card.append(node("p","Public problem explanations unavailable; this does not establish that no problems occurred."));
      if(item.timeline.length)card.append(table("Observation timeline — not a critical path or elapsed-time partition",["Event","Time","Evidence"],item.timeline.map((e)=>[human(e.kind),e.at,evidence(e.evidenceUrls)])));
      card.append(node("p",item.improvements.length?"Suggested next step (hypothesis): telemetry owner should collect missing native population and usage evidence; validate completeness before publishing a final item assessment.":"Public improvement proposals unavailable; private analyst prose is withheld."));
      body.append(card);
    });
  }
  ["efficiency-search","efficiency-scope","efficiency-view"].forEach((id)=>$(id).addEventListener(id==="efficiency-search"?"input":"change",()=>{page=0;render();}));
  $("efficiency-prev").addEventListener("click",()=>{page--;render();});$("efficiency-next").addEventListener("click",()=>{page++;render();});
  window.ProcessEfficiency={validate,render};
})();
