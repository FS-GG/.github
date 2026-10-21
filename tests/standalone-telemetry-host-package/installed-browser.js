"use strict";
const assert=require("node:assert/strict");
const {chromium}=require(process.argv[2]);
const [base,principal,key,workspace,item,spki,expectedApplied="1"]=process.argv.slice(3);
(async()=>{
  const browser=await chromium.launch({headless:true,args:[`--ignore-certificate-errors-spki-list=${spki}`]});
  try {
    const page=await browser.newPage({ignoreHTTPSErrors:false});
    const response=await page.goto(`${base}/private/dashboard/`);
    if(!response||response.status()!==200)throw new Error("dashboard unavailable");
    await page.locator('[name="principalId"]').fill(principal);
    await page.locator('[name="accessKey"]').fill(key);
    const snapshotResponse=page.waitForResponse(response=>new URL(response.url()).pathname.endsWith("/snapshot")&&response.request().method()==="POST"&&response.status()===200);
    await page.getByRole("button",{name:"Sign in"}).click();
    await page.locator(`#workspace option[value="${workspace}"]`).waitFor({state:"attached"});
    await page.getByRole("listitem").filter({hasText:item}).waitFor();
    const health=await page.locator("#health").innerText();
    const fields=health.split(" · ");
    if(!/^[1-9][0-9]{0,3}$/.test(expectedApplied)||fields[1]!==`${expectedApplied} applied receipts`||fields[2]!=="0 rejected receipts")throw new Error(`receipt health mismatch: ${JSON.stringify(health)}`);
    // Read the actual installed Host response, not a routed browser fixture.
    // The same assertion runs before shutdown and after isolated restore.
    const snapshot=await (await snapshotResponse).json();
    assert.equal(snapshot.schema,"fsgg.telemetry.private-dashboard/3");
    const usage=snapshot.unassignedUsage;
    assert.deepEqual(Object.keys(usage).sort(),["schema","populationCoverage","attribution","cost","counterEncoding","limit","truncated","observations"].sort());
    assert.equal(usage.schema,"fsgg.telemetry.unassigned-usage/1");
    assert.equal(usage.populationCoverage,"partial");
    assert.equal(usage.attribution,"unknown");
    assert.equal(usage.cost,"unknown");
    assert.equal(usage.counterEncoding,"decimal-int64");
    assert.equal(usage.limit,256);
    assert.equal(usage.truncated,false);
    assert.equal(usage.observations.length,1);
    const observation=usage.observations[0];
    assert.deepEqual(Object.keys(observation).sort(),["ordinal","revision","capturedAt","occurredAt","receivedAt","input","cachedInput","cacheWriteInput","output","reasoning","total"].sort());
    assert.deepEqual({ordinal:observation.ordinal,revision:observation.revision,input:observation.input,cachedInput:observation.cachedInput,cacheWriteInput:observation.cacheWriteInput,output:observation.output,reasoning:observation.reasoning,total:observation.total},
      {ordinal:1,revision:"2",input:"20",cachedInput:null,cacheWriteInput:"3",output:"2",reasoning:null,total:"22"});
    assert.equal(observation.occurredAt,null);
    await page.locator("#unassigned-rows .passive-row").waitFor();
    assert.equal(await page.locator("#unassigned-rows .passive-row").count(),1);
    assert.equal(await page.locator("#unassigned-rows h3").innerText(),"Observation 1 · revision 2");
    assert.equal(await page.locator("#unassigned-rows .passive-total").innerText(),"Observed total: 22");
    assert.match(await page.locator("#unassigned-rows").innerText(),/Input 20 · cached input unknown · cache writes 3 · output 2 · reasoning unknown/);
    assert.equal(await page.locator("#unassigned-status").innerText(),"1 displayed observations · partial population coverage · item attribution and billed cost: unknown. Counters are shown per observation without a combined total.");
    const visible=await page.locator("body").innerText();
    for(const privateValue of ["22222222-2222-2222-2222-222222222222","33333333-3333-3333-3333-333333333333","package-response"]){
      assert.equal(JSON.stringify(snapshot).includes(privateValue),false,"private native identity escaped the projection");
      assert.equal(visible.includes(privateValue),false,"private native identity escaped the browser");
    }
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exit(1);});
