(() => {
  "use strict";
  const $ = (id) => document.getElementById(id);
  const post = (path, body) => {const options={method:"POST",credentials:"same-origin",headers:{Accept:"application/json"},cache:"no-store"};if(body!==undefined){options.headers["Content-Type"]="application/json";options.body=JSON.stringify(body);}return fetch(path,options);};
  const text = (node, value) => { node.textContent = value; };
  const element = (name, value, className) => { const node=document.createElement(name); if(className)node.className=className; text(node,value); return node; };
  const count = (value) => value == null ? "unknown" : new Intl.NumberFormat().format(value);
  const tokenSummary = (item) => {
    const status=item.usage.nativeUsage;
    if(status==="missing"||!(["observed","incomplete","unsupported"].includes(status)))return "Native tokens unavailable";
    if(status==="unsupported")return "Native token reporting unsupported";
    const observed=`${count(item.usage.total)} observed native tokens`;
    if(status==="observed"&&item.coverage.populationCoverage==="complete"&&item.coverage.recordValidity==="complete"&&item.coverage.joinIntegrity==="complete")return `${count(item.usage.total)} native tokens`;
    if(status==="observed")return `${observed} · ${item.coverage.populationCoverage} population coverage`;
    return `${observed} · ${status} usage · ${item.coverage.populationCoverage} population coverage`;
  };
  const relationSummary = (item) => {
    if(item.state.memberRelation==="member"&&item.state.originalItemId)return `Canonical member of ${item.state.originalItemId}`;
    if(item.state.memberRelation==="original")return "Canonical original item";
    return "Canonical item · original relation unavailable";
  };
  const stepTime = (value) => {const parsed=Date.parse(value||"");return Number.isFinite(parsed)?parsed:null;};
  const durationText = (seconds) => seconds>=3600?`${Math.floor(seconds/3600)}h ${Math.floor(seconds%3600/60)}m ${seconds%60}s`:seconds>=60?`${Math.floor(seconds/60)}m ${seconds%60}s`:`${seconds}s`;
  const observedRuntime = (item) => {
    const steps=item.steps;
    if(!steps || !steps.runtimeCount)return "Observed item runtime: unknown";
    const rows=steps.rows.filter((row)=>row.kind==="runtime");
    const intervals=rows.map((row)=>({clock:row.clock,start:stepTime(row.startedAt),end:stepTime(row.endedAt)}))
      .filter((row)=>row.clock && row.clock!=="unknown" && row.start!==null && row.end!==null && row.end>=row.start);
    if(!intervals.length)return "Observed item runtime: unknown";
    const clocks=[...new Set(intervals.map((row)=>row.clock))];
    if(clocks.length!==1)return "Observed item runtime: unknown (multiple clock domains)";
    intervals.sort((a,b)=>a.start-b.start);
    let milliseconds=0,start=intervals[0].start,end=intervals[0].end;
    for(const row of intervals.slice(1)){
      if(row.start<=end)end=Math.max(end,row.end);
      else{milliseconds+=end-start;start=row.start;end=row.end;}
    }
    milliseconds+=end-start;
    const complete=rows.length===steps.runtimeCount && intervals.length===rows.length;
    const measured=durationText(Math.round(milliseconds/1000));
    return complete?`Observed item runtime: ${measured} (${clocks[0]} clock)`:
      `Observed runtime from displayed steps: ${measured} (${clocks[0]} clock · partial; ${intervals.length}/${steps.runtimeCount} timed invocations)`;
  };
  const stepGroup = (rows, title) => {
    const section=document.createElement("section");section.className="step-group";
    section.append(element("h5",title));
    const times=rows.flatMap((row)=>[stepTime(row.startedAt),stepTime(row.endedAt)]).filter((value)=>value!==null);
    const first=times.length?Math.min(...times):null;
    const last=times.length?Math.max(...times):null;
    const span=first!==null&&last>first?last-first:1;
    rows.slice().sort((a,b)=>(stepTime(a.startedAt)??Infinity)-(stepTime(b.startedAt)??Infinity)).forEach((row)=>{
      const start=stepTime(row.startedAt),end=stepTime(row.endedAt);
      const duration=start!==null&&end!==null&&end>=start?Math.round((end-start)/1000):null;
      const item=document.createElement("div");item.className="step-row";
      const label=element("span",`${row.kind === "ci" ? "CI" : row.kind === "activity" ? "Activity" : "Runtime"} · ${row.label}`,"step-label");item.append(label);
      const track=document.createElement("div");track.className="step-track";
      if(duration!==null&&first!==null){const bar=document.createElement("span");bar.className=`step-bar step-${row.kind}`;bar.style.left=`${Math.min(100,Math.max(0,100*(start-first)/span))}%`;bar.style.width=`${Math.max(1,Math.min(100,100*(end-start)/span))}%`;bar.title=`${row.label}: ${row.startedAt} to ${row.endedAt}`;track.append(bar);}
      else track.append(element("span","Time unknown","step-no-time"));
      item.append(track);
      const tokenText=row.tokenBasis==="not-applicable"?"tokens n/a":row.tokens===null?"tokens unknown":row.tokenBasis==="observed-native-partial"?`${count(row.tokens)} observed native tokens (coverage unproven)`:`${count(row.tokens)} directly attributed tokens (partial)`;
      item.append(element("span",`${row.classification} · ${duration===null?"duration unknown":durationText(duration)} · ${tokenText}`,"step-meta"));
      section.append(item);
    });
    return section;
  };
  const stepDetails = (item) => {
    const details=document.createElement("details");details.className="item-steps";
    const steps=item.steps;
    details.append(element("summary",`Pipeline · ${steps.runtimeCount} runtime · ${steps.activityCount} activity · ${steps.ciStepCount} CI steps`));
    const clocks=[...new Set(steps.rows.map((row)=>row.clock))];
    clocks.forEach((clock)=>details.append(stepGroup(steps.rows.filter((row)=>row.clock===clock),`${clock} clock`)));
    if(!steps.activityCount)details.append(element("p","No classified activity spans recorded; implementation and other work stages are unknown.","step-note"));
    if(!steps.ciStepCount)details.append(element("p","No CI steps recorded; CI time and classification are unknown.","step-note"));
    if(!steps.rows.length)details.append(element("p","No runtime invocations, activity spans or CI steps were recorded for this item. Step timing, classification and token attribution are unknown.","step-note"));
    if(steps.truncated)details.append(element("p",`Step display is limited to ${steps.limitPerKind} rows of each kind per item. Counts include omitted rows.`,"step-note"));
    details.append(element("p","Bars share a time scale only within one clock. Start order does not establish dependencies. Overlapping spans and runtime/activity tokens are not additive. Runtime relation (root, child, follow-up) is not work classification. Activity tokens include only direct attributions; runtime native token coverage may be incomplete.","step-note"));
    return details;
  };
  const local = document.querySelector('meta[name="fsgg-dashboard-mode"]')?.content === "local";
  const routes = local ? {session:"/api/session",snapshot:"/api/snapshot",logout:"/api/logout"} : {session:"/private/dashboard/v1/session/refresh",snapshot:"/private/dashboard/v1/snapshot",logout:"/private/dashboard/v1/logout"};
  let workspaces = [];
  function showLogin(failed = false) { $("dashboard").hidden=true; $("logout").hidden=true;if(local){$("login").hidden=true;$("local-ended").hidden=false;}else{$("login").hidden=false;$("login-error").hidden=!failed;} }
  function startSession(session) {
    if(session.schema!=="fsgg.telemetry.browser-session/1"||!Array.isArray(session.workspaces))throw new Error("invalid-session");
    workspaces=session.workspaces.slice(); const selector=$("workspace"); selector.replaceChildren();
    workspaces.forEach((workspace)=>{const option=document.createElement("option");option.value=workspace;text(option,workspace);selector.append(option);});
    $("login").hidden=true; $("local-ended").hidden=true; $("dashboard").hidden=false; $("logout").hidden=false;
  }
  function render(data) {
    text($("freshness"),data.observedAt?`Snapshot observed ${new Date(data.observedAt).toLocaleString()}`:"Observation time unavailable");
    const receipts=data.operational.appliedReceipts==null||data.operational.rejectedReceipts==null?"receipt outcomes unavailable":`${data.operational.appliedReceipts} applied receipts · ${data.operational.rejectedReceipts} rejected receipts`;
    text($("health"),`${data.operational.pendingBatches} pending receipts · ${receipts} · ${data.operational.consistency}`);
    const target=$("items");target.replaceChildren();
    const ordered=data.items.slice().sort((a,b)=>Number(b.state.outcome==="delivered"&&b.state.population==="completed")-Number(a.state.outcome==="delivered"&&a.state.population==="completed"));
    ordered.forEach((item)=>{
      const card=document.createElement("article");card.setAttribute("role","listitem");
      card.append(element("h3",item.id));
      card.append(element("p",relationSummary(item),"relation"));
      card.append(element("p",`${item.state.outcome} · ${item.state.population}`,"state"));
      const totals=document.createElement("div");totals.className="item-totals";
      totals.append(element("p",observedRuntime(item),"item-time"));
      totals.append(element("p",tokenSummary(item),"item-token"));
      card.append(totals);
      card.append(element("p",`${count(item.runtime.terminal)}/${count(item.runtime.admitted)} terminal`,"metric"));
      card.append(element("p",`coverage ${item.coverage.populationCoverage} · CI ${item.coverage.ciInventory}`,"coverage"));
      if(item.state.outcome==="missing")card.append(element("p","No outcome or delivery observation was recorded for this item.","step-note"));
      if(item.coverage.populationCoverage==="unknown")card.append(element("p","Population completeness has not been established; observed time and tokens may be partial.","step-note"));
      const activity=item.steps?item.steps.rows.filter((row)=>row.kind==="activity"):[];
      const classes=[...new Set(activity.map((row)=>row.classification))];
      card.append(element("p",classes.length?`Observed activity classes: ${classes.join(", ")}${item.steps.truncated?" (display truncated)":" (recording coverage unknown)"}`:"Observed activity classes: unknown","step-note"));
      if(item.steps){
        const details=stepDetails(item);
        details.open=item.state.outcome==="delivered"&&item.state.population==="completed";
        card.classList.toggle("expanded",details.open);
        details.addEventListener("toggle",()=>card.classList.toggle("expanded",details.open));
        card.append(details);
      }
      target.append(card);
    });
    if(!data.items.length)target.append(element("p","No scoped observations are available."));
  }
  async function load(){const error=$("error");error.hidden=true;const response=await post(routes.snapshot,{workspaceId:$("workspace").value});if(response.status===401){showLogin();return;}if(!response.ok){error.hidden=false;text(error,response.status===404?"The selected workspace is unavailable.":"The private telemetry snapshot is unavailable.");return;}render(await response.json());}
  $("login").addEventListener("submit",async(event)=>{event.preventDefault();const loginForm=event.currentTarget;const form=new FormData(loginForm);try{const response=await post("/private/dashboard/login",{principalId:String(form.get("principalId")||""),accessKey:String(form.get("accessKey")||"")});loginForm.elements.accessKey.value="";if(!response.ok){showLogin(true);return;}startSession(await response.json());await load();}catch(_){loginForm.elements.accessKey.value="";showLogin(true);}});
  $("workspace").addEventListener("change",()=>{load().catch(()=>{const error=$("error");error.hidden=false;text(error,"The private telemetry snapshot is unavailable.");});});
  $("logout").addEventListener("click",async()=>{try{await post(routes.logout);}finally{workspaces=[];showLogin();}});
  (async()=>{try{const response=await post(routes.session);if(response.status===401){showLogin();return;}if(!response.ok)throw new Error("refresh");startSession(await response.json());await load();}catch(_){showLogin();}})();
})();
