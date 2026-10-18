"use strict";
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
    await page.getByRole("button",{name:"Sign in"}).click();
    await page.locator(`#workspace option[value="${workspace}"]`).waitFor({state:"attached"});
    await page.getByRole("listitem").filter({hasText:item}).waitFor();
    const health=await page.locator("#health").innerText();
    const fields=health.split(" · ");
    if(!/^[1-9][0-9]{0,3}$/.test(expectedApplied)||fields[1]!==`${expectedApplied} applied receipts`||fields[2]!=="0 rejected receipts")throw new Error(`receipt health mismatch: ${JSON.stringify(health)}`);
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exit(1);});
