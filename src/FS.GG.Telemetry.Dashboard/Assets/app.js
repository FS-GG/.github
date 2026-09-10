(() => {
  "use strict";
  const $ = (id) => document.getElementById(id);
  const post = (path, body = {}) => fetch(path, {method:"POST",credentials:"same-origin",headers:{Accept:"application/json","Content-Type":"application/json"},cache:"no-store",body:JSON.stringify(body)});
  const text = (node, value) => { node.textContent = value; };
  const element = (name, value, className) => { const node=document.createElement(name); if(className)node.className=className; text(node,value); return node; };
  const count = (value) => value == null ? "unknown" : new Intl.NumberFormat().format(value);
  let workspaces = [];
  function showLogin(failed = false) { $("dashboard").hidden=true; $("logout").hidden=true; $("login").hidden=false; $("login-error").hidden=!failed; }
  function startSession(session) {
    if(session.schema!=="fsgg.telemetry.browser-session/1"||!Array.isArray(session.workspaces))throw new Error("invalid-session");
    workspaces=session.workspaces.slice(); const selector=$("workspace"); selector.replaceChildren();
    workspaces.forEach((workspace)=>{const option=document.createElement("option");option.value=workspace;text(option,workspace);selector.append(option);});
    $("login").hidden=true; $("dashboard").hidden=false; $("logout").hidden=false;
  }
  function render(data) {
    text($("freshness"),data.observedAt?`Snapshot observed ${new Date(data.observedAt).toLocaleString()}`:"Observation time unavailable");
    const receipts=data.operational.appliedReceipts==null||data.operational.rejectedReceipts==null?"receipt outcomes unavailable":`${data.operational.appliedReceipts} applied receipts · ${data.operational.rejectedReceipts} rejected receipts`;
    text($("health"),`${data.operational.pendingBatches} pending receipts · ${receipts} · ${data.operational.consistency}`);
    const target=$("items");target.replaceChildren();
    data.items.forEach((item)=>{const card=document.createElement("article");card.setAttribute("role","listitem");card.append(element("h3",item.id));card.append(element("p",`${item.state.outcome} · ${item.state.population}`,"state"));card.append(element("p",`${count(item.usage.total)} tokens · ${count(item.runtime.terminal)}/${count(item.runtime.admitted)} terminal`,"metric"));card.append(element("p",`coverage ${item.coverage.populationCoverage} · CI ${item.coverage.ciInventory}`,"coverage"));target.append(card);});
    if(!data.items.length)target.append(element("p","No scoped observations are available."));
  }
  async function load(){const error=$("error");error.hidden=true;const response=await post("/private/dashboard/v1/snapshot",{workspaceId:$("workspace").value});if(response.status===401){showLogin();return;}if(!response.ok){error.hidden=false;text(error,response.status===404?"The selected workspace is unavailable.":"The private telemetry snapshot is unavailable.");return;}render(await response.json());}
  $("login").addEventListener("submit",async(event)=>{event.preventDefault();const loginForm=event.currentTarget;const form=new FormData(loginForm);try{const response=await post("/private/dashboard/login",{principalId:String(form.get("principalId")||""),accessKey:String(form.get("accessKey")||"")});loginForm.elements.accessKey.value="";if(!response.ok){showLogin(true);return;}startSession(await response.json());await load();}catch(_){loginForm.elements.accessKey.value="";showLogin(true);}});
  $("workspace").addEventListener("change",()=>{load().catch(()=>{const error=$("error");error.hidden=false;text(error,"The private telemetry snapshot is unavailable.");});});
  $("logout").addEventListener("click",async()=>{try{await post("/private/dashboard/v1/logout");}finally{workspaces=[];showLogin();}});
  (async()=>{try{const response=await post("/private/dashboard/v1/session/refresh");if(response.status===401){showLogin();return;}if(!response.ok)throw new Error("refresh");startSession(await response.json());await load();}catch(_){showLogin();}})();
})();
