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
      const label=element("span",row.label,"step-label");item.append(label);
      const track=document.createElement("div");track.className="step-track";
      if(duration!==null&&first!==null){const bar=document.createElement("span");bar.className="step-bar";bar.style.left=`${Math.min(100,Math.max(0,100*(start-first)/span))}%`;bar.style.width=`${Math.max(1,Math.min(100,100*(end-start)/span))}%`;track.append(bar);}
      else track.append(element("span","Time unknown","step-no-time"));
      item.append(track);
      const tokenText=row.tokenBasis==="not-applicable"?"tokens n/a":row.tokens===null?"tokens unknown":`${count(row.tokens)} directly attributed tokens (partial)`;
      item.append(element("span",`${row.classification} · ${duration===null?"duration unknown":`${duration}s`} · ${tokenText}`,"step-meta"));
      section.append(item);
    });
    return section;
  };
  const stepDetails = (item) => {
    const details=document.createElement("details");details.className="item-steps";
    const steps=item.steps; const activity=steps.rows.filter((row)=>row.kind==="activity"),ci=steps.rows.filter((row)=>row.kind==="ci");
    details.append(element("summary",`Observed steps · ${steps.activityCount} activities · ${steps.ciStepCount} CI steps`));
    if(activity.length)details.append(element("p",`Observed activity classes: ${[...new Set(activity.map((row)=>row.classification))].join(", ")}. This list is partial if spans were not recorded.`,"step-note"));
    const activityClocks=[...new Set(activity.map((row)=>row.clock))];
    activityClocks.forEach((clock)=>details.append(stepGroup(activity.filter((row)=>row.clock===clock),`Activity spans · ${clock} clock`)));
    if(ci.length)details.append(stepGroup(ci,"CI steps"));
    if(!steps.rows.length)details.append(element("p","No activity spans or CI steps were recorded for this item. Step timing, classification and token attribution are unknown.","step-note"));
    if(steps.truncated)details.append(element("p",`Step display is limited to ${steps.limitPerKind} activity spans and ${steps.limitPerKind} CI steps per item. Counts include omitted rows.`,"step-note"));
    details.append(element("p","Bars share a time scale within each section. Start order does not establish dependencies. Overlapping spans are not additive. Activity tokens include only direct attributions and may omit other usage.","step-note"));
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
    data.items.forEach((item)=>{const card=document.createElement("article");card.setAttribute("role","listitem");card.append(element("h3",item.id));card.append(element("p",relationSummary(item),"relation"));card.append(element("p",`${item.state.outcome} · ${item.state.population}`,"state"));card.append(element("p",`${tokenSummary(item)} · ${count(item.runtime.terminal)}/${count(item.runtime.admitted)} terminal`,"metric"));card.append(element("p",`coverage ${item.coverage.populationCoverage} · CI ${item.coverage.ciInventory}`,"coverage"));if(item.state.outcome==="missing"||item.coverage.populationCoverage==="unknown")card.append(element("p","Missing means no outcome or delivery observation was recorded. Unknown coverage means no completeness proof was recorded for this item.","step-note"));if(item.steps){const details=stepDetails(item);details.addEventListener("toggle",()=>card.classList.toggle("expanded",details.open));card.append(details);}target.append(card);});
    if(!data.items.length)target.append(element("p","No scoped observations are available."));
  }
  async function load(){const error=$("error");error.hidden=true;const response=await post(routes.snapshot,{workspaceId:$("workspace").value});if(response.status===401){showLogin();return;}if(!response.ok){error.hidden=false;text(error,response.status===404?"The selected workspace is unavailable.":"The private telemetry snapshot is unavailable.");return;}render(await response.json());}
  $("login").addEventListener("submit",async(event)=>{event.preventDefault();const loginForm=event.currentTarget;const form=new FormData(loginForm);try{const response=await post("/private/dashboard/login",{principalId:String(form.get("principalId")||""),accessKey:String(form.get("accessKey")||"")});loginForm.elements.accessKey.value="";if(!response.ok){showLogin(true);return;}startSession(await response.json());await load();}catch(_){loginForm.elements.accessKey.value="";showLogin(true);}});
  $("workspace").addEventListener("change",()=>{load().catch(()=>{const error=$("error");error.hidden=false;text(error,"The private telemetry snapshot is unavailable.");});});
  $("logout").addEventListener("click",async()=>{try{await post(routes.logout);}finally{workspaces=[];showLogin();}});
  (async()=>{try{const response=await post(routes.session);if(response.status===401){showLogin();return;}if(!response.ok)throw new Error("refresh");startSession(await response.json());await load();}catch(_){showLogin();}})();
})();
