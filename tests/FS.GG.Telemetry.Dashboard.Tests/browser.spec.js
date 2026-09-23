const {test,expect}=require("@playwright/test");
const fs=require("node:fs");
const path=require("node:path");
const assets=path.resolve(__dirname,"../../src/FS.GG.Telemetry.Dashboard/Assets");
const session={schema:"fsgg.telemetry.browser-session/1",workspaces:["workspace-a","workspace-b"]};
const snapshot=(workspace)=>({schema:"fsgg.telemetry.private-dashboard/1",workspaceId:workspace,observedAt:"2026-09-10T09:00:00Z",revision:"a".repeat(64),operational:{pendingBatches:2,appliedReceipts:3,rejectedReceipts:1,consistency:"database-transaction"},items:[{id:"<img src=x onerror=alert(1)>",factCount:1,usageObservations:0,deliveryObservations:0,usage:{input:0,cachedInput:0,cacheWriteInput:0,output:0,total:0,reasoning:null,nativeUsage:"missing"},runtime:{admitted:0,started:0,terminal:0,usage:0,missingAdmission:0,missingStart:0,missingTerminal:0,missingUsage:0,gapCodes:[]},state:{population:"missing",dirty:false,outcome:"missing",codeDelivery:"unknown",observedAt:null},coverage:{recordValidity:"unknown",joinIntegrity:"unknown",populationCoverage:"unknown",qualification:"not-evaluated",ciInventory:"unknown",ciChecks:"unknown",ciAttempts:"unknown",ciJobs:"unknown",ciTerminal:"unknown",ciTimestamps:"unknown",ciContinuation:"none",externalChecks:null},clockProvenance:[]} ]});

test("mocked transport UI journey covers login, scoped refresh, expiry and logout",async({page})=>{
  let authenticated=false, expired=false, loginBody=null, snapshots=[];
  await page.route("https://telemetry.test/private/dashboard/**",async route=>{
    const request=route.request(),url=new URL(request.url());
    if(request.method()==="GET"){
      const file=url.pathname.endsWith("app.js")?"app.js":url.pathname.endsWith("styles.css")?"styles.css":"index.html";
      const contentType=file.endsWith(".js")?"text/javascript":file.endsWith(".css")?"text/css":"text/html";
      return route.fulfill({status:200,contentType,body:fs.readFileSync(path.join(assets,file))});
    }
    const body=request.postDataJSON();
    if(url.pathname.endsWith("/session/refresh"))return route.fulfill({status:authenticated?200:401,json:session});
    if(url.pathname.endsWith("/login")){loginBody=body;authenticated=true;return route.fulfill({status:200,json:session});}
    if(url.pathname.endsWith("/snapshot")){if(expired)return route.fulfill({status:401});snapshots.push(body);const data=snapshot(body.workspaceId);const seed=data.items[0];data.items.push({...seed,id:"partial-item",usage:{...seed.usage,total:14,nativeUsage:"observed"},coverage:{...seed.coverage,populationCoverage:"partial"}});data.items.push({...seed,id:"unjoined-item",usage:{...seed.usage,total:9,nativeUsage:"observed"},coverage:{...seed.coverage,populationCoverage:"complete"}});data.items.push({...seed,id:"complete-item",state:{...seed.state,memberRelation:"member",originalItemId:"original-item"},usage:{...seed.usage,total:21,nativeUsage:"observed"},coverage:{...seed.coverage,populationCoverage:"complete",recordValidity:"complete",joinIntegrity:"complete"}});return route.fulfill({status:body.workspaceId==="workspace-b"?404:200,json:data});}
    if(url.pathname.endsWith("/logout")){authenticated=false;return route.fulfill({status:204});}
    return route.fulfill({status:404});
  });
  await page.goto("https://telemetry.test/private/dashboard/");
  await expect(page.locator("#login")).toBeVisible();
  await page.locator('[name="principalId"]').fill("reader-a");await page.locator('[name="accessKey"]').fill("secret-never-in-url");await page.getByRole("button",{name:"Sign in"}).click();
  await expect(page.locator("#dashboard")).toBeVisible();expect(loginBody).toEqual({principalId:"reader-a",accessKey:"secret-never-in-url"});expect(page.url()).not.toContain("secret-never-in-url");await expect(page.locator('[name="accessKey"]')).toHaveValue("");
  await expect(page.locator("#workspace option")).toHaveCount(2);await expect(page.locator("article h3")).toHaveText(["<img src=x onerror=alert(1)>","partial-item","unjoined-item","complete-item"]);await expect(page.locator("article img")).toHaveCount(0);await expect(page.locator("article .metric")).toContainText(["Native tokens unavailable","14 observed native tokens · partial population coverage","9 observed native tokens · complete population coverage","21 native tokens"]);await expect(page.locator("#health")).toContainText("2 pending receipts · 3 applied receipts · 1 rejected receipts");expect(snapshots[0]).toEqual({workspaceId:"workspace-a"});
  await expect(page.locator("article .relation")).toContainText(["original relation unavailable","original relation unavailable","original relation unavailable","Canonical member of original-item"]);
  await page.locator("#workspace").selectOption("workspace-b");await expect(page.locator("#error")).toContainText("selected workspace is unavailable");
  expired=true;await page.locator("#workspace").selectOption("workspace-a");await expect(page.locator("#login")).toBeVisible();
  expired=false;await page.locator('[name="principalId"]').fill("reader-a");await page.locator('[name="accessKey"]').fill("new-secret");await page.getByRole("button",{name:"Sign in"}).click();await page.getByRole("button",{name:"Sign out"}).click();await expect(page.locator("#login")).toBeVisible();
});

test("item steps render real browser bars with honest token and missing-data labels",async({page})=>{
  const data=snapshot("workspace-a");
  const observed=data.items[0];observed.id="observed-item";
  observed.steps={runtimeCount:1,activityCount:1,ciStepCount:1,truncated:false,limitPerKind:20,rows:[
    {kind:"runtime",label:"Root invocation",classification:"root",clock:"host-wall",startedAt:"2026-09-10T08:00:00Z",endedAt:"2026-09-10T08:02:00Z",tokens:10,tokenBasis:"observed-native-partial"},
    {kind:"activity",label:"Implementation",classification:"implementation",clock:"host-wall",startedAt:"2026-09-10T08:00:30Z",endedAt:"2026-09-10T08:01:30Z",tokens:6,tokenBasis:"direct-attribution-partial"},
    {kind:"ci",label:"CI step 1",classification:"useful-validation",clock:"github",startedAt:"2026-09-10T08:03:00Z",endedAt:"2026-09-10T08:04:00Z",tokens:null,tokenBasis:"not-applicable"}
  ]};
  const missing={...snapshot("workspace-a").items[0],id:"unobserved-item",steps:{runtimeCount:0,activityCount:0,ciStepCount:0,truncated:false,limitPerKind:20,rows:[]}};
  data.items.push(missing);
  await page.route("https://telemetry.test/private/dashboard/**",async route=>{
    const request=route.request(),url=new URL(request.url());
    if(request.method()==="GET"){
      const file=url.pathname.endsWith("app.js")?"app.js":url.pathname.endsWith("styles.css")?"styles.css":"index.html";
      return route.fulfill({status:200,contentType:file.endsWith(".js")?"text/javascript":file.endsWith(".css")?"text/css":"text/html",body:fs.readFileSync(path.join(assets,file))});
    }
    if(url.pathname.endsWith("/session/refresh"))return route.fulfill({status:200,json:session});
    if(url.pathname.endsWith("/snapshot"))return route.fulfill({status:200,json:data});
    return route.fulfill({status:404});
  });
  await page.goto("https://telemetry.test/private/dashboard/");
  await expect(page.locator("article")).toHaveCount(2);
  const card=page.locator("article").first();
  await expect(card.locator(".item-steps summary")).toContainText("1 runtime invocations · 1 activities · 1 CI steps");
  await card.locator(".item-steps summary").click();
  await expect(card.locator(".step-group h5")).toHaveText(["Runtime invocations · host-wall clock","Activity spans · host-wall clock","CI steps"]);
  await expect(card.locator(".step-bar")).toHaveCount(3);
  const barWidths=await card.locator(".step-bar").evaluateAll(bars=>bars.map(bar=>bar.getBoundingClientRect().width));
  expect(barWidths.every(width=>width>0)).toBe(true);
  await expect(card.locator(".step-row").nth(0)).toContainText("root · 120s · 10 observed native tokens (coverage unproven)");
  await expect(card.locator(".step-row").nth(1)).toContainText("implementation · 60s · 6 directly attributed tokens (partial)");
  await expect(card.locator(".step-row").nth(2)).toContainText("useful-validation · 60s · tokens n/a");
  const empty=page.locator("article").nth(1);
  await expect(empty).toContainText("Observed activity classes: unknown");
  await empty.locator(".item-steps summary").click();
  await expect(empty).toContainText("No runtime invocations, activity spans or CI steps were recorded");
});
