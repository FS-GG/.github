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
  await expect(page.getByText("Requested (Medium) → Observed (High)")).toBeVisible();
  await expect(page.getByText("Cause, repair time, repair tokens")).toBeVisible();
  await expect(page.getByRole("link",{name:"evidence ↗",exact:true})).toHaveAttribute("href","https://github.com/FS-GG/.github/pull/1");
  await expect(page.getByLabel("CI measurements; categories may overlap").getByRole("cell",{name:"Unknown"})).toBeVisible();
  await page.locator("#item-search").fill("no match");
  await expect(page.locator("#deliveries tr")).toHaveCount(0);
  await page.setViewportSize({width:390,height:844});
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth)).toBeTruthy();
});
