const { test, expect } = require("@playwright/test");
function payload(host, observedAt = "2026-09-09T08:00:00Z") {
  const runs = Array.from({ length: 8 }, (_, i) => ({
    id: 100 + i,
    workflow: i === 0 ? "<img src=x onerror=alert(1)>" : "Coord checks",
    event: "pull_request",
    status: "completed",
    conclusion: i === 1 ? "failure" : "success",
    createdAt: `2026-09-09T07:0${i}:00Z`,
    startedAt: `2026-09-09T07:0${i}:01Z`,
    updatedAt: `2026-09-09T07:0${i}:31Z`,
    durationSeconds: 30,
    attempt: 1,
    url: `https://github.com/FS-GG/.github/actions/runs/${100 + i}`,
  }));
  return {
    schema: "fsgg.telemetry.dashboard/2",
    builtAt: observedAt,
    sourceRevision: "a".repeat(40),
    hostRevision: null,
    actions: {
      schema: "fsgg.telemetry.public-actions/1",
      observedAt,
      repository: "FS-GG/.github",
      selection: {
        order: "created-descending",
        cap: 1000,
        pagesFetched: 1,
        returned: runs.length,
        repositoryTotalAtObservation: 40000,
        truncated: true,
        newestCreatedAt: runs[7].createdAt,
        oldestCreatedAt: runs[0].createdAt,
        semantics:
          "bounded multi-page sample, deduplicated by run id; latest observed attempt; not an atomic inventory",
      },
      runs,
    },
    deliveries: {
      schema: "fsgg.telemetry.public-deliveries/1",
      observedAt,
      repository: "FS-GG/.github",
      selection: { order: "closed-updated-descending", cap: 200, pagesFetched: 1, closedScanned: 2, returned: 2, semantics: "merged pull requests found in a bounded updated-ordered closed-PR scan; public delivery evidence, not proof of a whole completed item or effort" },
      deliveries: [1,2].map((number)=>({number,title:number===1?"Telemetry dashboard":"Docs",url:`https://github.com/FS-GG/.github/pull/${number}`,createdAt:"2026-09-09T07:00:00Z",mergedAt:"2026-09-09T08:00:00Z",elapsedSeconds:3600})),
    },
    host,
  };
}
function completedHost(keys = ["one", "two"]) {
  const items = keys.map((key, index) => ({
    key,
    label: `Completed ${key}`,
    url: `https://github.com/FS-GG/.github/pull/${index + 1}`,
    deliveredAt: "2026-09-09T08:00:00Z",
    deliveries: [],
    runtime: {
      invocations: 1,
      duration: { rows: [{ role: "root", invocations: 1, known: 1, unknown: 0, summedSeconds: 30 }] },
      tokens: { coverage: { invocationsWithUsage: 0, invocationsWithoutUsage: 1, runtimeGaps: 1 }, rows: [] },
    },
    ci: { counts: { runs: 0 }, seconds: {} },
    budget: { assessments: [] },
    complications: { observed: {}, notes: [] },
  }));
  return {
    schema: "fsgg.telemetry.dashboard-host/5",
    revision: "a".repeat(64),
    sourceDeliveries: {schema:"fsgg.telemetry.source-deliveries/1",coverage:{eligible:0,published:0,unmapped:0,dirty:0,incompatible:0},items:[]},
    processEfficiency: {schema:"fsgg.telemetry.process-efficiency/1",policyVersion:"efficiency-public-projection/1",source:"unavailable",status:"unavailable",coverage:{published:0,unmapped:0,withheld:0,unsupported:0},exports:[],items:[]},
    observedAt: "2026-09-09T08:00:00Z",
    totals: { usageObservations: 0 },
    usage: { input: 0, cachedInput: 0, cacheWriteInput: 0, output: 0, reasoning: null, total: 0 },
    launcherPopulation: { admitted: 2, terminal: 2 },
    quality: {}, operational: { expected: 2, lineage: {}, timing: {} },
    localCi: { counts: { runs: 0, jobs: 0 }, seconds: {}, coverage: {} },
    store: { status: "ready", schemaVersion: 14, journalMode: "wal", pendingBatches: 0 },
    budget: { distinctBreaches: 0, intervention: "none", dirtyItems: 0, health: {}, dimensions: {}, assessments: [] },
    completedItems: { coverage: { eligible: items.length, published: items.length, unmapped: 0, dirty: 0, incompatible: 0 }, items },
  };
}
function currentHostFixture(host) {
  const current=completedHost([]);
  return {...current,...host,schema:current.schema,revision:current.revision,
    store:{...host.store,schemaVersion:14},sourceDeliveries:current.sourceDeliveries,
    processEfficiency:current.processEfficiency};
}
async function expectMobileContainment(page) {
  const observed=await page.evaluate(()=>({viewport:document.documentElement.clientWidth,width:document.documentElement.scrollWidth,
    overflow:[...document.querySelectorAll("body *")].filter((e)=>e.getBoundingClientRect().right>document.documentElement.clientWidth+1).slice(0,12).map((e)=>({tag:e.tagName,id:e.id,right:e.getBoundingClientRect().right}))}));
  expect(observed.width,JSON.stringify(observed)).toBeLessThanOrEqual(observed.viewport);
}

test("populated, keyboard, text equivalent, XSS and mobile layout", async ({
  page,
}) => {
  await page.route("**/data/dashboard.json", (route) =>
    route.fulfill({
      json: payload({
        schema: "fsgg.telemetry.dashboard-host-unavailable/1",
        status: "unconfigured",
        reason: "missing",
      }),
    }),
  );
  const errors = [];
  page.on("pageerror", (e) => errors.push(e.message));
  await page.goto("/");
  await expect(page.getByText("40K at observation")).toBeVisible();
  await expect(page.locator("#trend-desc")).toContainText("Values:");
  expect(await page.locator("img").count()).toBe(0);
  await page.locator("#search").fill("coord");
  await expect(page.locator("#runs tr")).toHaveCount(7);
  await page.keyboard.press("Tab");
  await page.setViewportSize({ width: 390, height: 844 });
  await expectMobileContainment(page);
  await expect(page.locator("nav")).toBeVisible();
  await page.locator("#theme").click();
  expect(errors).toEqual([]);
});
test("malformed and unobserved data fail visibly", async ({ page }) => {
  await page.route("**/data/dashboard.json", (route) =>
    route.fulfill({
      json: {
        schema: "fsgg.telemetry.dashboard/2",
        actions: { schema: "bad", runs: [] },
      },
    }),
  );
  await page.goto("/");
  await expect(page.locator("#error")).toBeVisible();
  await expect(page.locator("#health-label")).toHaveText("Data unavailable");
});
test("subitem pipeline shows approved nodes, partial values and unknowns without inferred allocation", async ({page}) => {
  const host=completedHost(["one"]);
  host.completedItems.items[0].pipeline={schema:"fsgg.telemetry.item-pipeline/2",coverage:{eligible:3,published:2,unmapped:1},nodes:[
    {key:"root",label:"Approved planning item",url:"https://github.com/FS-GG/.github/issues/1",parentKey:null,status:"settled",stage:"planning",workClass:"unknown",time:{status:"known",seconds:60,basis:"same-clock-invocation-union",open:0,missing:0,overlap:"no"},tokens:{status:"complete",total:120,attribution:"direct"}},
    {key:"child",label:"Approved implementation item",url:"https://github.com/FS-GG/.github/issues/2",parentKey:null,status:"settled",stage:"implementation",workClass:"unclassified",time:{status:"partial",seconds:30,basis:"same-clock-invocation-union",open:1,missing:1,overlap:"yes"},tokens:{status:"unknown",total:null,attribution:"unknown"}},
  ]};
  await page.route("**/data/dashboard.json",route=>route.fulfill({json:payload(host)}));
  await page.goto("/#item-one");
  const pipeline=page.getByRole("list",{name:"Approved subitems and observed measurements"});
  await expect(pipeline.getByRole("listitem")).toHaveCount(2);
  await expect(page.getByText("2/3 canonical member items have approved public nodes; 1 remain unmapped",{exact:false})).toBeVisible();
  await expect(pipeline.getByRole("link",{name:"Approved implementation item"})).toHaveAttribute("href","https://github.com/FS-GG/.github/issues/2");
  await expect(pipeline.getByText("Observed time: 30s known portion",{exact:false})).toBeVisible();
  await expect(pipeline.getByText("overlap yes · 1 open · 1 missing",{exact:false})).toBeVisible();
  await expect(pipeline.getByText("Native tokens: Unknown",{exact:false})).toBeVisible();
  await expect(page.getByText("Order does not establish parentage or dependencies",{exact:false})).toBeVisible();
  await page.setViewportSize({width:390,height:844});
  await expectMobileContainment(page);
});
test("subitem pipeline rejects an unapproved evidence URL", async ({page}) => {
  const host=completedHost(["one"]);
  host.completedItems.items[0].pipeline={schema:"fsgg.telemetry.item-pipeline/1",coverage:{eligible:1,published:1,unmapped:0},nodes:[
    {key:"root",label:"Approved item",url:"https://github.com/FS-GG/.github/issues/1/extra",parentKey:null,status:"settled",stage:"planning",workClass:"unknown",time:{status:"unknown",seconds:null},tokens:{status:"unknown",total:null,attribution:"unknown"}},
  ]};
  await page.route("**/data/dashboard.json",route=>route.fulfill({json:payload(host)}));
  await page.goto("/#item-one");
  await expect(page.locator("#error")).toBeVisible();
});
test("completed item drilldown preserves unknowns, evidence links and mobile access", async ({page}) => {
  const unavailable={schema:"fsgg.telemetry.dashboard-host-unavailable/1",status:"unconfigured",reason:"missing"};
  const data=payload(unavailable);
  data.host={schema:"fsgg.telemetry.dashboard-host/2",observedAt:"2026-09-09T08:00:00Z",totals:{usageObservations:1},usage:{input:100,cachedInput:40,cacheWriteInput:0,output:20,reasoning:null,total:120},launcherPopulation:{admitted:2,terminal:2},quality:{},operational:{expected:2,lineage:{},timing:{}},localCi:{counts:{runs:1,jobs:1},seconds:{},coverage:{}},store:{status:"ready",schemaVersion:7,journalMode:"wal",pendingBatches:0},budget:{distinctBreaches:0,intervention:"none",dirtyItems:0,health:{},dimensions:{},assessments:[]},completedItems:{coverage:{eligible:1,published:1,unmapped:0,dirty:0,incompatible:0},items:[{key:"item-one",label:"Telemetry item",url:"https://github.com/FS-GG/.github/issues/1",deliveredAt:"2026-09-09T08:00:00Z",deliveries:[],runtime:{invocations:2,duration:{rows:[{role:"root",invocations:1,known:1,unknown:0,summedSeconds:100},{role:"child",invocations:1,known:0,unknown:1,summedSeconds:0}]},tokens:{unmappedRows:0,coverage:{invocationsWithUsage:1,invocationsWithoutUsage:1,runtimeGaps:0},rows:[{role:"root",requestedModel:"Requested",observedModel:"Observed",requestedEffort:"Medium",observedEffort:"High",scope:"Host A",turns:1,input:100,cachedInput:40,output:20,reasoning:null,total:120}]}},ci:{counts:{runs:1},seconds:{runnerSeconds:{knownItems:1,unknownItems:0,totalItemSeconds:60},queueSeconds:{knownItems:0,unknownItems:1,totalItemSeconds:0}}},budget:{assessments:[]},complications:{observed:{runtimeNonSuccess:1,failedOrCancelledCiRuns:1,repeatedCiRuns:0,followUpInvocations:0},notes:[{kind:"complication",text:"Documented issue",evidenceUrl:"https://github.com/FS-GG/.github/pull/1"}]}}]}};
  data.host=currentHostFixture(data.host);
  await page.route("**/data/dashboard.json",route=>route.fulfill({json:data}));
  await page.goto("/#item-item-one");
  await expect(page.locator("#item-item-one")).toHaveAttribute("open","");
  await expect(page.getByText("Observed tokens by compatible scope · legacy coverage",{exact:true})).toBeVisible();
  await expect(page.getByText("Requested (Medium) → Observed (High)")).toBeVisible();
  await expect(page.getByText("Repair time and repair tokens remain unknown",{exact:false})).toBeVisible();
  await expect(page.getByRole("link",{name:"evidence ↗",exact:true})).toHaveAttribute("href","https://github.com/FS-GG/.github/pull/1");
  await expect(page.getByLabel("CI measurements; categories may overlap").getByRole("cell",{name:"Unknown"})).toBeVisible();
  await page.locator("#item-search").fill("no match");
  await expect(page.locator("#deliveries tr")).toHaveCount(0);
  await page.setViewportSize({width:390,height:844});
  await expectMobileContainment(page);
});
test("schema-8 process detail shows activity, attribution, complications, reviews and truncation", async ({page}) => {
  const data=payload({schema:"fsgg.telemetry.dashboard-host/3",observedAt:"2026-09-09T09:00:00Z",totals:{usageObservations:0},usage:{input:0,cachedInput:0,cacheWriteInput:0,output:0,reasoning:null,total:0},launcherPopulation:{admitted:1,terminal:1},quality:{},operational:{expected:1,lineage:{},timing:{}},localCi:{counts:{runs:0,jobs:0},seconds:{},coverage:{}},store:{status:"ready",schemaVersion:8,journalMode:"wal",pendingBatches:0},budget:{distinctBreaches:0,intervention:"none",dirtyItems:0,health:{},dimensions:{},assessments:[]},completedItems:{coverage:{eligible:1,published:1,unmapped:0,dirty:0,incompatible:0},items:[{key:"schema-eight",label:"Schema eight detail",url:"https://github.com/FS-GG/.github/issues/8",deliveredAt:"2026-09-09T08:30:00Z",deliveries:[],runtime:{invocations:1,duration:{rows:[]},tokens:{unmappedRows:0,coverage:{invocationsWithUsage:0,invocationsWithoutUsage:1,runtimeGaps:1},rows:[]}},ci:{counts:{runs:0},seconds:{}},budget:{assessments:[]},complications:{observed:{runtimeNonSuccess:0,failedOrCancelledCiRuns:0,repeatedCiRuns:0,followUpInvocations:0},notes:[]},process:{availability:"available",members:{requested:1,available:1},truncated:{activities:false,attributions:false,complications:true,reviews:false},activities:{summary:[{category:"repair",spans:1,open:0,knownDuration:1,summedSeconds:60}],rows:[{category:"repair",startedAt:"2026-09-09T08:00:00Z",endedAt:"2026-09-09T08:01:00Z",durationSeconds:60}]},attribution:{rows:[],accounting:{nativeTotal:0,direct:0,mixed:0,unclassified:0,missingAttribution:0},crossRead:"matched"},complications:{rows:[{trigger:"test-failure",cause:"product-defect",activityCategory:"repair",occurredAt:"2026-09-09T08:01:00Z"},{trigger:"review-finding",cause:"process-defect",activityCategory:null,occurredAt:"2026-09-09T08:02:00Z"}]},reviews:{rows:[{scope:"attempt",revision:2,confidence:"high",evidenceCoverage:"partial",populationCoverage:"complete",reviewerModel:"Observed",reviewerEffort:"High",reviewedAt:"2026-09-09T08:03:00Z",durationSeconds:30,counts:{wentWell:1,problems:1,avoidableDelayOrRework:0,processObservations:1,remainingRisks:0,concreteImprovements:1}}]}}}]}});
  Object.assign(data.host.completedItems.items[0].runtime.tokens.coverage,{boundary:"canonical completed member items and all expected runtime dispatches",status:"unknown",expectedDispatches:2,linkedInvocations:2,admittedInvocations:2,startedInvocations:2,terminalInvocations:2,invocationsWithUsage:0,invocationsWithoutUsage:2,runtimeGaps:2,accountingCompatibility:"none"});
  data.host=currentHostFixture(data.host);
  await page.route("**/data/dashboard.json",route=>route.fulfill({json:data})); await page.goto("/#item-schema-eight");
  await expect(page.getByText("Token coverage unknown · 0/2 with usage",{exact:true})).toBeVisible();
  await expect(page.getByText("Repair · 1m 0s")).toBeVisible();
  await expect(page.getByText("Truncated: complications")).toBeVisible();
  await expect(page.getByText("Product Defect",{exact:true})).toBeVisible();
  await expect(page.getByText("Attempt · r2",{exact:true})).toBeVisible();
  await expect(page.getByText("High confidence does not prove item completeness")).toBeVisible();
  await expect(page.getByText("1 native usage row(s) missing",{exact:false})).toHaveCount(0);
  await page.setViewportSize({width:390,height:844});
  await expectMobileContainment(page);
});

test("aggregate usage is labelled observed and warns when coverage may be partial", async ({page}) => {
  const data=payload({schema:"fsgg.telemetry.dashboard-host-unavailable/1",status:"unconfigured",reason:"missing"});
  data.host={schema:"fsgg.telemetry.dashboard-host/3",observedAt:"2026-09-09T09:00:00Z",totals:{usageObservations:7},usage:{input:700,cachedInput:200,cacheWriteInput:0,output:200,reasoning:null,total:900},launcherPopulation:{admitted:24,started:24,terminal:24,usage:7,missingAdmission:0,missingStart:0,missingTerminal:0,missingUsage:17},quality:{},operational:{expected:24,lineage:{},timing:{}},localCi:{counts:{runs:0},seconds:{},coverage:{}},store:{status:"ready",schemaVersion:8,journalMode:"wal",pendingBatches:0},budget:{distinctBreaches:0,intervention:"none",dirtyItems:1,health:{},dimensions:{},assessments:[]},completedItems:{schema:"fsgg.telemetry.completed-items/2",coverage:{eligible:0,published:0,unmapped:0,dirty:1,incompatible:0},items:[]}};
  data.host=currentHostFixture(data.host);
  await page.route("**/data/dashboard.json",route=>route.fulfill({json:data})); await page.goto("/");
  await expect(page.getByRole("heading",{name:"Observed tokens"})).toBeVisible();
  await expect(page.getByText("coverage gaps can make this a partial total",{exact:false})).toBeVisible();
});

test("minute refresh replaces data while preserving interaction state", async ({ page }) => {
  await page.clock.install();
  let requests = 0;
  await page.route("**/data/dashboard.json", async (route) => {
    requests += 1;
    const data = payload(requests < 4 ? completedHost() : { schema: "fsgg.telemetry.dashboard-host-unavailable/1", status: "unconfigured", reason: "missing" });
    if (requests === 3) {
      data.actions.runs = data.actions.runs.filter((run) => run.conclusion === "success");
      data.actions.selection.returned = data.actions.runs.length;
      data.sourceRevision = "b".repeat(40);
    }
    await route.fulfill({ json: data });
  });
  await page.goto("/#item-one");
  await expect(page.locator("#item-one")).toHaveAttribute("open", "");
  await page.locator("#item-two summary").click();
  await page.locator("#local-content details").first().locator("summary").click();
  await page.evaluate(() => history.replaceState(null, "", "#item-one"));
  await page.locator("#search").fill("coord");
  await page.locator("#outcome-filter").selectOption("failure");
  await page.locator("#search").focus();
  await page.clock.runFor(60000);
  await expect.poll(() => requests).toBeGreaterThanOrEqual(2);
  await expect(page.locator("#item-one")).toHaveAttribute("open", "");
  await expect(page.locator("#item-two")).toHaveAttribute("open", "");
  await expect(page.locator("#local-content details").first()).toHaveAttribute("open", "");
  expect(await page.evaluate(() => location.hash)).toBe("#item-one");
  await expect(page.locator("#search")).toBeFocused();
  await expect(page.locator("#search")).toHaveValue("coord");
  await expect(page.locator("#outcome-filter")).toHaveValue("failure");
  await page.clock.runFor(60000);
  await expect.poll(() => requests).toBeGreaterThanOrEqual(3);
  await expect(page.locator("#outcome-filter option:checked")).toContainText("(0)");
  await page.clock.runFor(60000);
  await expect.poll(() => requests).toBeGreaterThanOrEqual(4);
  await expect(page.locator("#local-content")).toBeEmpty();
  await expect(page.locator(".item-card")).toHaveCount(0);
  await expect(page.locator("#refresh-status")).toContainText("checking every minute");
});

test("malformed refresh retains last good data and a later success recovers", async ({ page }) => {
  await page.clock.install();
  let requests = 0;
  await page.route("**/data/dashboard.json", async (route) => {
    requests += 1;
    const data = payload(completedHost(["stable"]));
    if (requests === 2) data.host.localCi.seconds = { runnerSeconds: null };
    if (requests >= 3) data.actions.runs[0].workflow = "Recovered workflow";
    await route.fulfill({ json: data });
  });
  await page.goto("/");
  await expect(page.locator("#m-runs")).toHaveText("8");
  await page.clock.runFor(60000);
  await expect.poll(() => requests).toBeGreaterThanOrEqual(2);
  await expect(page.locator("#error")).toContainText("showing last good data");
  await expect(page.locator("#m-runs")).toHaveText("8");
  await expect(page.getByText("Completed stable", { exact: true })).toBeVisible();
  await page.clock.runFor(60000);
  await expect.poll(() => requests).toBeGreaterThanOrEqual(3);
  await expect(page.locator("#error")).toBeHidden();
  await expect(page.getByRole("link", { name: "Recovered workflow" })).toBeVisible();
});

test("initial older feed stays unavailable; matching host5 honors the original deep link", async ({ page }) => {
  await page.clock.install();
  let requests=0;
  await page.route("**/data/dashboard.json",(route)=>{
    requests+=1;
    if(requests===1){const old=completedHost(["recovered"]);old.schema="fsgg.telemetry.dashboard-host/3";return route.fulfill({json:payload(old)});}
    return route.fulfill({json:payload(completedHost(["recovered"]))});
  });
  await page.goto("/#item-recovered");
  await expect(page.locator("#health-label")).toHaveText("Data unavailable");
  await expect(page.locator("#error")).toContainText("Host schema 5 is required");
  await expect(page.locator(".item-card")).toHaveCount(0);
  await page.clock.runFor(60000);
  await expect(page.locator("#item-recovered")).toHaveAttribute("open","");
});

test("hidden pages pause checks, resume overdue, and requests never overlap", async ({ page }) => {
  await page.clock.install();
  await page.addInitScript(() => {
    const nativeFetch=window.fetch.bind(window);
    window.__refreshProbe={requests:0,active:0,peak:0};
    window.fetch=(...args)=>{
      const probe=window.__refreshProbe;
      probe.requests+=1;
      if (probe.requests===1) return nativeFetch(...args);
      probe.active+=1; probe.peak=Math.max(probe.peak,probe.active);
      return new Promise((resolve,reject)=>args[1].signal.addEventListener("abort",()=>{probe.active-=1;reject(new DOMException("Aborted","AbortError"));},{once:true}));
    };
  });
  await page.route("**/data/dashboard.json", (route) => route.fulfill({ json: payload({ schema: "fsgg.telemetry.dashboard-host-unavailable/1", status: "unconfigured", reason: "missing" }) }));
  await page.goto("/");
  await page.evaluate(() => {
    window.__dashboardHidden = true;
    Object.defineProperty(document, "hidden", { configurable: true, get: () => window.__dashboardHidden });
    document.dispatchEvent(new Event("visibilitychange"));
  });
  await page.clock.runFor(61000);
  expect(await page.evaluate(()=>window.__refreshProbe.requests)).toBe(1);
  await page.evaluate(() => { window.__dashboardHidden = false; document.dispatchEvent(new Event("visibilitychange")); });
  await expect.poll(() => page.evaluate(()=>window.__refreshProbe.requests)).toBeGreaterThanOrEqual(2);
  await page.clock.runFor(10000);
  await expect(page.locator("#error")).toContainText("timed out");
  expect(await page.evaluate(()=>window.__refreshProbe.peak)).toBe(1);
});

function sourceDeliveredHost() {
  const host=completedHost(["one","two","three","four","five"]);
  host.schema="fsgg.telemetry.dashboard-host/5";
  host.revision="a".repeat(64);
  host.sourceDeliveries={schema:"fsgg.telemetry.source-deliveries/1",coverage:{eligible:1,published:1,unmapped:0,dirty:0,incompatible:0},items:[{
    key:"governance-ci",label:"Governance CI",url:"https://github.com/FS-GG/governance_config/pull/444",state:"source-delivered",operationalCompletion:"unestablished",deliveredAt:"2026-10-05T19:00:00Z",
    deliveries:[{repository:"FS-GG/governance_config",number:444,url:"https://github.com/FS-GG/governance_config/pull/444",mergedAt:"2026-10-05T19:00:00Z"}]
  }]};
  return host;
}

test("host5 source delivery is separate from five completed items and exposes no cost",async({page})=>{
  await page.route("**/data/dashboard.json",route=>route.fulfill({json:payload(sourceDeliveredHost())}));
  await page.goto("/");
  await expect(page.locator(".item-card")).toHaveCount(5);
  await expect(page.locator("#source-deliveries article")).toHaveCount(1);
  await expect(page.locator("#source-deliveries")).toContainText("operational completion unestablished");
  await expect(page.locator("#source-deliveries a").last()).toHaveAttribute("href","https://github.com/FS-GG/governance_config/pull/444");
  await expect(page.locator("#source-deliveries")).not.toContainText("0 tokens");
  await expect(page.locator("#provenance")).toContainText("a".repeat(64));
});

test("malformed fields and prior host schema keep last valid host5; matching host5 recovers",async({page})=>{
  await page.clock.install(); let requests=0;
  await page.route("**/data/dashboard.json",route=>{
    requests++;const host=sourceDeliveredHost();
    if(requests===2) host.sourceDeliveries.items[0].privateNotes="PRIVATE SENTINEL";
    if(requests===3) host.revision="bad";
    if(requests===4) host.schema="fsgg.telemetry.dashboard-host/3";
    return route.fulfill({json:payload(host)});
  });
  await page.goto("/");
  // Initial refresh.finally updates this status and arms the next timer in one task.
  // Navigation alone can finish before the routed fetch has settled.
  await expect(page.locator("#refresh-status")).toContainText("Checked");
  await expect(page.locator("#refresh-status")).toContainText("checking every minute");
  await expect(page.locator(".item-card")).toHaveCount(5);
  await expect(page.locator("#source-deliveries article")).toHaveCount(1);
  for(let i=0;i<3;i++){
    await page.clock.runFor(60000);await expect.poll(()=>requests).toBeGreaterThanOrEqual(i+2);
    await expect(page.locator("#error")).toContainText("showing last good data");
    await expect(page.locator("#source-deliveries article")).toHaveCount(1);
    await expect(page.locator("body")).not.toContainText("PRIVATE SENTINEL");
  }
  await page.clock.runFor(60000);await expect.poll(()=>requests).toBeGreaterThanOrEqual(5);
  await expect(page.locator("#error")).toBeHidden();
  await expect(page.locator("#source-deliveries article")).toHaveCount(1);
  await expect(page.locator(".item-card")).toHaveCount(5);
});

function efficiencyHost() {
  const host=completedHost([]);
  host.schema="fsgg.telemetry.dashboard-host/5";host.revision="a".repeat(64);
  host.sourceDeliveries={schema:"fsgg.telemetry.source-deliveries/1",coverage:{eligible:0,published:0,unmapped:0,dirty:0,incompatible:0},items:[]};
  host.processEfficiency=JSON.parse(JSON.stringify(require("./fixtures/process-efficiency-public-v1.json")));
  return host;
}

test("host5 efficiency fixture tables, unknown accounting, keyboard and bounded filters",async({page})=>{
  const host=efficiencyHost(),feed=host.processEfficiency;
  for(let i=0;i<11;i++){const item=JSON.parse(JSON.stringify(feed.items.find((item)=>item.scope==="native-item")));item.key=`page-${i}`;item.label=`Synthetic page ${i}`;feed.items.push(item);}
  feed.coverage.published=feed.items.length;
  await page.route("**/data/dashboard.json",route=>route.fulfill({json:payload(host)}));
  await page.goto("/#efficiency");
  await expect(page.locator("#efficiency-health")).toContainText("Fixture preview");
  await expect(page.locator("#efficiency-items details")).toHaveCount(10);
  const summary=page.locator("#efficiency-missing-example-toggle");await summary.focus();await page.keyboard.press("Enter");
  const incomplete=page.locator("#efficiency-missing-example");
  await expect(incomplete).toContainText("runtime accounting is incomplete");
  await expect(incomplete).toContainText("unknown: Unknown");
  await expect(incomplete.locator("caption").first()).toContainText("Canonical measurements");
  await expect(incomplete.locator("th[scope=col]").first()).toBeVisible();
  await page.locator("#efficiency-next").click();await expect(page.locator("#efficiency-items details")).toHaveCount(3);
  await page.selectOption("#efficiency-scope","provisional-delivery");await expect(page.locator("#efficiency-items details")).toHaveCount(1);
  await expect(page.locator("#efficiency-page")).toContainText("Page 1 of 1");
  await page.locator("#efficiency-search").fill("absent");await expect(page.locator("#efficiency-items")).toContainText("No approved items match");
});

test("host5 malformed efficiency refresh preserves the last valid explanation",async({page})=>{
  await page.clock.install();let requests=0;
  await page.route("**/data/dashboard.json",route=>{const host=efficiencyHost();if(++requests>1)host.processEfficiency.items[0].privateNotes="PRIVATE SENTINEL";return route.fulfill({json:payload(host)});});
  await page.goto("/#efficiency");
  await page.locator("#efficiency-missing-example-toggle").click();
  await page.clock.runFor(60000);await expect.poll(()=>requests).toBeGreaterThanOrEqual(2);
  await expect(page.locator("#error")).toContainText("showing last good data");
  await expect(page.locator("#efficiency-missing-example")).toHaveAttribute("open","");
  await expect(page.locator("#efficiency-items")).not.toContainText("PRIVATE SENTINEL");
});

test("canonical efficiency export shows queued analysis, omitted populations and unknown clocks",async({page})=>{
  const host=efficiencyHost();host.processEfficiency=JSON.parse(JSON.stringify(require("./fixtures/process-efficiency-canonical-public-v1.json")));
  await page.route("**/data/dashboard.json",route=>route.fulfill({json:payload(host)}));
  await page.goto("/#efficiency");
  await expect(page.locator("#efficiency-health")).toContainText("Canonical export");
  await expect(page.locator("#efficiency-health")).toContainText("3 omitted");
  await page.locator("#efficiency-native-example-toggle").click();
  await expect(page.locator("#efficiency-native-example")).toContainText("Request: pending · accepted assessment: ready");
  await expect(page.locator("#efficiency-native-example")).toContainText("ingested: unknown");
  await page.locator("#efficiency-missing-example-toggle").click();
  await expect(page.locator("#efficiency-missing-example")).toContainText("4 omitted");
  await expect(page.locator("#efficiency-missing-example")).toContainText("Selected measurements unavailable");
  await expect(page.locator("#efficiency-missing-example")).toContainText("Source observed: unknown");
  await expect(page.locator("#efficiency-items")).not.toContainText("PRIVATE");
});

test("analysis status filters current requests, retains the filter on refresh and clears unavailable counts",async({page})=>{
  await page.clock.install();let requests=0;
  await page.route("**/data/dashboard.json",route=>{
    const host=efficiencyHost();
    host.processEfficiency=JSON.parse(JSON.stringify(require("./fixtures/process-efficiency-canonical-public-v1.json")));
    requests++;
    if(requests===2){
      const item=host.processEfficiency.items.find((row)=>row.key==="native-example");
      item.analysisState="failed";item.exportHealth.requestState="failed";item.exportHealth.failureCode="timeout";
    }
    if(requests>2)host.processEfficiency={schema:"fsgg.telemetry.process-efficiency/1",policyVersion:"efficiency-public-projection/1",source:"unavailable",status:"unavailable",coverage:{published:0,unmapped:0,withheld:0,unsupported:0},exports:[],items:[]};
    return route.fulfill({json:payload(host)});
  });
  await page.goto("/#efficiency");
  await expect(page.locator("#refresh-status")).toContainText("checking every minute");
  await expect(page.locator("#efficiency-analysis-counts")).toContainText("2 approved rows");
  await expect(page.locator("#efficiency-analysis-counts")).toContainText("pending: 2");
  await expect(page.locator("#efficiency-analysis-counts")).toContainText("ready: 0");
  await expect(page.locator("#efficiency-analysis-counts")).toContainText("exclude unmapped, withheld and source-omitted");
  await expect(page.locator("#efficiency-native-example-toggle")).toContainText("analysis pending");
  await page.selectOption("#efficiency-scope","native-item");
  await expect(page.locator("#efficiency-analysis-counts")).toContainText("1 approved row");
  await page.selectOption("#efficiency-scope","all");
  await page.selectOption("#efficiency-analysis-state","failed");
  await expect(page.locator("#efficiency-items")).toContainText("No approved items match");
  await page.clock.runFor(60000);await expect.poll(()=>requests).toBe(2);
  await expect(page.locator("#efficiency-analysis-state")).toHaveValue("failed");
  await expect(page.locator("#efficiency-items details")).toHaveCount(1);
  const summary=page.locator("#efficiency-native-example-toggle");await summary.focus();await page.keyboard.press("Enter");
  await expect(page.locator("#efficiency-native-example")).toContainText("Request: failed · accepted assessment: ready");
  await expect(page.locator("#efficiency-analysis-counts")).toContainText("failed: 1");
  await page.setViewportSize({width:390,height:844});
  await expect(page.locator("#efficiency-health")).toContainText(`sha256:${"b".repeat(64)}`);
  const provenanceWidth=await page.locator("#efficiency-health").evaluate((element)=>({width:element.clientWidth,scrollWidth:element.scrollWidth}));
  expect(provenanceWidth.scrollWidth).toBeLessThanOrEqual(provenanceWidth.width);
  await expectMobileContainment(page);
  await page.clock.runFor(60000);await expect.poll(()=>requests).toBe(3);
  await expect(page.locator("#efficiency-analysis-counts")).toHaveText("Analysis population unavailable; missing evidence does not establish zero work.");
  await expect(page.locator("#efficiency-items details")).toHaveCount(0);
});

function contextHost(historical,current) {
  historical.source={kind:"configured-local-store",publicExportSchema:"fsgg.telemetry.public-export/1"};
  if(current)current.source={kind:"configured-local-store",publicExportSchema:"fsgg.telemetry.public-export/1"};
  return {schema:"fsgg.telemetry.dashboard-host/5",observedAt:"2026-09-09T08:00:00Z",revision:"c".repeat(64),source:{kind:"independent-local-stores"},contexts:[
    {key:"historical",label:"Historical",status:"ready",reason:null,host:historical},
    {key:"current",label:"Current work",status:current?"ready":"unavailable",reason:current?null:"source-unavailable",host:current}
  ]};
}

test("independent Historical and Current work selection retains completed work and source uncertainty",async({page})=>{
  await page.clock.install();let requests=0;
  const historical=efficiencyHost(),current=efficiencyHost();
  historical.completedItems=completedHost(["history-done"]).completedItems;
  historical.processEfficiency.items[0].label="Historical approved item";
  current.processEfficiency.items[0].label="Current approved item";
  historical.usage.total=111;current.usage.total=222;
  await page.route("**/data/dashboard.json",route=>{requests++;return route.fulfill({json:payload(contextHost(historical,requests>1?null:current))});});
  await page.goto("/#efficiency");
  await expect(page.locator("#source-context-note")).toContainText("Current work: one independent source");
  await expect(page.locator("#efficiency-items")).toContainText("Current approved item");
  await expect(page.locator("#efficiency-items")).not.toContainText("Historical approved item");
  await page.selectOption("#source-context-select","historical");
  await expect(page.locator("#efficiency-items")).toContainText("Historical approved item");
  await expect(page.locator("#completed-items")).toContainText("Completed history-done");
  await page.clock.runFor(60000);await expect.poll(()=>requests).toBeGreaterThan(1);
  await expect(page.locator("#source-context-select")).toHaveValue("historical");
  await expect(page.locator("#efficiency-items")).toContainText("Historical approved item");
  await page.selectOption("#source-context-select","current");
  await expect(page.locator("#source-context-note")).toContainText("Work counts are unknown");
  await expect(page.locator("#local-state")).toHaveText("Host unavailable");
  await expect(page.locator("#items-note")).toContainText("Host item projection unavailable");
  await expect(page.locator("#efficiency-items")).not.toContainText("Historical approved item");
  await page.setViewportSize({width:390,height:844});await expectMobileContainment(page);
});

test("malformed source context refresh retains validated selected source",async({page})=>{
  await page.clock.install();let requests=0;
  await page.route("**/data/dashboard.json",route=>{const host=contextHost(efficiencyHost(),efficiencyHost());if(++requests>1)host.contexts[1].label="PRIVATE WORKSPACE";return route.fulfill({json:payload(host)});});
  await page.goto("/#efficiency");await page.selectOption("#source-context-select","historical");
  await page.clock.runFor(60000);await expect.poll(()=>requests).toBeGreaterThan(1);
  await expect(page.locator("#error")).toContainText("showing last good data");
  await expect(page.locator("#source-context-select")).toHaveValue("historical");
  await expect(page.locator("#source-context-note")).toContainText("Historical: one independent source");
  await expect(page.locator("#source-context")).not.toContainText("PRIVATE WORKSPACE");
});

test("source selector is hidden for single and unavailable feeds and visible for independent contexts",async({page})=>{
  await page.clock.install();let requests=0;
  await page.route("**/data/dashboard.json",route=>{
    const host=++requests===1?efficiencyHost():requests===2?{schema:"fsgg.telemetry.dashboard-host-unavailable/1",status:"unconfigured"}:contextHost(efficiencyHost(),efficiencyHost());
    return route.fulfill({json:payload(host)});
  });
  await page.goto("/#efficiency");await expect(page.locator("#source-context")).toBeHidden();
  await page.clock.runFor(60000);await expect.poll(()=>requests).toBe(2);
  await expect(page.locator("#source-context")).toBeHidden();
  await expect(page.locator("#local-state")).toHaveText("Host unconfigured");
  await page.clock.runFor(60000);await expect.poll(()=>requests).toBe(3);
  await expect(page.locator("#source-context")).toBeVisible();
  await expect(page.locator("#source-context-select")).toHaveValue("current");
  await expect(page.locator("#source-context-note")).toContainText("Current work: one independent source");
});
