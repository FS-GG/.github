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
    if(url.pathname.endsWith("/snapshot")){if(expired)return route.fulfill({status:401});snapshots.push(body);return route.fulfill({status:body.workspaceId==="workspace-b"?404:200,json:snapshot(body.workspaceId)});}
    if(url.pathname.endsWith("/logout")){authenticated=false;return route.fulfill({status:204});}
    return route.fulfill({status:404});
  });
  await page.goto("https://telemetry.test/private/dashboard/");
  await expect(page.locator("#login")).toBeVisible();
  await page.locator('[name="principalId"]').fill("reader-a");await page.locator('[name="accessKey"]').fill("secret-never-in-url");await page.getByRole("button",{name:"Sign in"}).click();
  await expect(page.locator("#dashboard")).toBeVisible();expect(loginBody).toEqual({principalId:"reader-a",accessKey:"secret-never-in-url"});expect(page.url()).not.toContain("secret-never-in-url");await expect(page.locator('[name="accessKey"]')).toHaveValue("");
  await expect(page.locator("#workspace option")).toHaveCount(2);await expect(page.locator("article h3")).toHaveText("<img src=x onerror=alert(1)>");await expect(page.locator("article img")).toHaveCount(0);await expect(page.locator("#health")).toContainText("2 pending receipts · 3 applied receipts · 1 rejected receipts");expect(snapshots[0]).toEqual({workspaceId:"workspace-a"});
  await page.locator("#workspace").selectOption("workspace-b");await expect(page.locator("#error")).toContainText("selected workspace is unavailable");
  expired=true;await page.locator("#workspace").selectOption("workspace-a");await expect(page.locator("#login")).toBeVisible();
  expired=false;await page.locator('[name="principalId"]').fill("reader-a");await page.locator('[name="accessKey"]').fill("new-secret");await page.getByRole("button",{name:"Sign in"}).click();await page.getByRole("button",{name:"Sign out"}).click();await expect(page.locator("#login")).toBeVisible();
});
