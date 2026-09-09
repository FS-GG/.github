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
    schema: "fsgg.telemetry.dashboard-host/2",
    observedAt: "2026-09-09T08:00:00Z",
    totals: { usageObservations: 0 },
    usage: { input: 0, cachedInput: 0, cacheWriteInput: 0, output: 0, reasoning: null, total: 0 },
    launcherPopulation: { admitted: 2, terminal: 2 },
    quality: {}, operational: { expected: 2, lineage: {}, timing: {} },
    localCi: { counts: { runs: 0, jobs: 0 }, seconds: {}, coverage: {} },
    store: { status: "ready", schemaVersion: 8, journalMode: "wal", pendingBatches: 0 },
    budget: { distinctBreaches: 0, intervention: "none", dirtyItems: 0, health: {}, dimensions: {}, assessments: [] },
    completedItems: { coverage: { eligible: items.length, published: items.length, unmapped: 0, dirty: 0, incompatible: 0 }, items },
  };
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
  expect(
    await page.evaluate(
      () =>
        document.documentElement.scrollWidth <=
        document.documentElement.clientWidth,
    ),
  ).toBeTruthy();
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
test("completed item drilldown preserves unknowns, evidence links and mobile access", async ({page}) => {
  const unavailable={schema:"fsgg.telemetry.dashboard-host-unavailable/1",status:"unconfigured",reason:"missing"};
  const data=payload(unavailable);
  data.host={schema:"fsgg.telemetry.dashboard-host/2",observedAt:"2026-09-09T08:00:00Z",totals:{usageObservations:1},usage:{input:100,cachedInput:40,cacheWriteInput:0,output:20,reasoning:null,total:120},launcherPopulation:{admitted:2,terminal:2},quality:{},operational:{expected:2,lineage:{},timing:{}},localCi:{counts:{runs:1,jobs:1},seconds:{},coverage:{}},store:{status:"ready",schemaVersion:7,journalMode:"wal",pendingBatches:0},budget:{distinctBreaches:0,intervention:"none",dirtyItems:0,health:{},dimensions:{},assessments:[]},completedItems:{coverage:{eligible:1,published:1,unmapped:0,dirty:0,incompatible:0},items:[{key:"item-one",label:"Telemetry item",url:"https://github.com/FS-GG/.github/issues/1",deliveredAt:"2026-09-09T08:00:00Z",deliveries:[],runtime:{invocations:2,duration:{rows:[{role:"root",invocations:1,known:1,unknown:0,summedSeconds:100},{role:"child",invocations:1,known:0,unknown:1,summedSeconds:0}]},tokens:{unmappedRows:0,coverage:{invocationsWithUsage:1,invocationsWithoutUsage:1,runtimeGaps:0},rows:[{role:"root",requestedModel:"Requested",observedModel:"Observed",requestedEffort:"Medium",observedEffort:"High",scope:"Host A",turns:1,input:100,cachedInput:40,output:20,reasoning:null,total:120}]}},ci:{counts:{runs:1},seconds:{runnerSeconds:{knownItems:1,unknownItems:0,totalItemSeconds:60},queueSeconds:{knownItems:0,unknownItems:1,totalItemSeconds:0}}},budget:{assessments:[]},complications:{observed:{runtimeNonSuccess:1,failedOrCancelledCiRuns:1,repeatedCiRuns:0,followUpInvocations:0},notes:[{kind:"complication",text:"Documented issue",evidenceUrl:"https://github.com/FS-GG/.github/pull/1"}]}}]}};
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
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth)).toBeTruthy();
});
test("schema-8 process detail shows activity, attribution, complications, reviews and truncation", async ({page}) => {
  const data=payload({schema:"fsgg.telemetry.dashboard-host/3",observedAt:"2026-09-09T09:00:00Z",totals:{usageObservations:0},usage:{input:0,cachedInput:0,cacheWriteInput:0,output:0,reasoning:null,total:0},launcherPopulation:{admitted:1,terminal:1},quality:{},operational:{expected:1,lineage:{},timing:{}},localCi:{counts:{runs:0,jobs:0},seconds:{},coverage:{}},store:{status:"ready",schemaVersion:8,journalMode:"wal",pendingBatches:0},budget:{distinctBreaches:0,intervention:"none",dirtyItems:0,health:{},dimensions:{},assessments:[]},completedItems:{coverage:{eligible:1,published:1,unmapped:0,dirty:0,incompatible:0},items:[{key:"schema-eight",label:"Schema eight detail",url:"https://github.com/FS-GG/.github/issues/8",deliveredAt:"2026-09-09T08:30:00Z",deliveries:[],runtime:{invocations:1,duration:{rows:[]},tokens:{unmappedRows:0,coverage:{invocationsWithUsage:0,invocationsWithoutUsage:1,runtimeGaps:1},rows:[]}},ci:{counts:{runs:0},seconds:{}},budget:{assessments:[]},complications:{observed:{runtimeNonSuccess:0,failedOrCancelledCiRuns:0,repeatedCiRuns:0,followUpInvocations:0},notes:[]},process:{availability:"available",members:{requested:1,available:1},truncated:{activities:false,attributions:false,complications:true,reviews:false},activities:{summary:[{category:"repair",spans:1,open:0,knownDuration:1,summedSeconds:60}],rows:[{category:"repair",startedAt:"2026-09-09T08:00:00Z",endedAt:"2026-09-09T08:01:00Z",durationSeconds:60}]},attribution:{rows:[],accounting:{nativeTotal:0,direct:0,mixed:0,unclassified:0,missingAttribution:0},crossRead:"matched"},complications:{rows:[{trigger:"test-failure",cause:"product-defect",activityCategory:"repair",occurredAt:"2026-09-09T08:01:00Z"},{trigger:"review-finding",cause:"process-defect",activityCategory:null,occurredAt:"2026-09-09T08:02:00Z"}]},reviews:{rows:[{scope:"attempt",revision:2,confidence:"high",evidenceCoverage:"partial",populationCoverage:"complete",reviewerModel:"Observed",reviewerEffort:"High",reviewedAt:"2026-09-09T08:03:00Z",durationSeconds:30,counts:{wentWell:1,problems:1,avoidableDelayOrRework:0,processObservations:1,remainingRisks:0,concreteImprovements:1}}]}}}]}});
  Object.assign(data.host.completedItems.items[0].runtime.tokens.coverage,{boundary:"canonical completed member items and all expected runtime dispatches",status:"unknown",expectedDispatches:2,linkedInvocations:2,admittedInvocations:2,startedInvocations:2,terminalInvocations:2,invocationsWithUsage:0,invocationsWithoutUsage:2,runtimeGaps:2,accountingCompatibility:"none"});
  await page.route("**/data/dashboard.json",route=>route.fulfill({json:data})); await page.goto("/#item-schema-eight");
  await expect(page.getByText("Token coverage unknown · 0/2 with usage",{exact:true})).toBeVisible();
  await expect(page.getByText("Repair · 1m 0s")).toBeVisible();
  await expect(page.getByText("Truncated: complications")).toBeVisible();
  await expect(page.getByText("Product Defect",{exact:true})).toBeVisible();
  await expect(page.getByText("Attempt · r2",{exact:true})).toBeVisible();
  await expect(page.getByText("High confidence does not prove item completeness")).toBeVisible();
  await expect(page.getByText("1 native usage row(s) missing",{exact:false})).toHaveCount(0);
  await page.setViewportSize({width:390,height:844});
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth)).toBeTruthy();
});

test("aggregate usage is labelled observed and warns when coverage may be partial", async ({page}) => {
  const data=payload({schema:"fsgg.telemetry.dashboard-host-unavailable/1",status:"unconfigured",reason:"missing"});
  data.host={schema:"fsgg.telemetry.dashboard-host/3",observedAt:"2026-09-09T09:00:00Z",totals:{usageObservations:7},usage:{input:700,cachedInput:200,cacheWriteInput:0,output:200,reasoning:null,total:900},launcherPopulation:{admitted:24,started:24,terminal:24,usage:7,missingAdmission:0,missingStart:0,missingTerminal:0,missingUsage:17},quality:{},operational:{expected:24,lineage:{},timing:{}},localCi:{counts:{runs:0},seconds:{},coverage:{}},store:{status:"ready",schemaVersion:8,journalMode:"wal",pendingBatches:0},budget:{distinctBreaches:0,intervention:"none",dirtyItems:1,health:{},dimensions:{},assessments:[]},completedItems:{schema:"fsgg.telemetry.completed-items/2",coverage:{eligible:0,published:0,unmapped:0,dirty:1,incompatible:0},items:[]}};
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

test("first successful retry honors the original item deep link", async ({ page }) => {
  await page.clock.install();
  let requests=0;
  await page.route("**/data/dashboard.json",(route)=>{
    requests+=1;
    if(requests===1) return route.fulfill({json:{schema:"bad"}});
    return route.fulfill({json:payload(completedHost(["recovered"]))});
  });
  await page.goto("/#item-recovered");
  await expect(page.locator("#health-label")).toHaveText("Data unavailable");
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
