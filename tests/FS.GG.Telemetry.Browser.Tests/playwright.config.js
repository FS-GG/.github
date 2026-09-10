const spki=process.env.FSGG_BROWSER_CERTIFICATE_SPKI;
if(!spki)throw new Error("FSGG_BROWSER_CERTIFICATE_SPKI is required");
module.exports={testDir:".",testMatch:"browser.real.spec.js",timeout:30000,use:{browserName:"chromium",headless:true,ignoreHTTPSErrors:false,launchOptions:{args:[`--ignore-certificate-errors-spki-list=${spki}`]}}};
