const {test,expect}=require("@playwright/test");

const baseURL=process.env.FSGG_BROWSER_BASE_URL;
const principalId=process.env.FSGG_BROWSER_PRINCIPAL_ID;
const accessKey=process.env.FSGG_BROWSER_ACCESS_KEY;
const knownWorkspace=process.env.FSGG_BROWSER_KNOWN_WORKSPACE;
const unavailableWorkspace=process.env.FSGG_BROWSER_UNAVAILABLE_WORKSPACE;

for(const [name,value] of Object.entries({FSGG_BROWSER_BASE_URL:baseURL,FSGG_BROWSER_PRINCIPAL_ID:principalId,FSGG_BROWSER_ACCESS_KEY:accessKey,FSGG_BROWSER_KNOWN_WORKSPACE:knownWorkspace,FSGG_BROWSER_UNAVAILABLE_WORKSPACE:unavailableWorkspace}))
  if(!value)throw new Error(`${name} is required`);
if(!baseURL.startsWith("https://"))throw new Error("FSGG_BROWSER_BASE_URL must be HTTPS");

test("real Host browser journey uses scoped secure session",async({page,context})=>{
  const posts=[];
  page.on("request",request=>{if(request.method()==="POST")posts.push({url:request.url(),origin:request.headers().origin});});
  const response=await page.goto(`${baseURL}/private/dashboard/`);
  expect(response.status()).toBe(200);
  expect(response.headers()["content-security-policy"]).toContain("default-src 'none'");
  expect(response.headers()["x-content-type-options"]).toBe("nosniff");
  await expect(page.locator("#login")).toBeVisible();
  await page.locator('[name="principalId"]').fill(principalId);
  await page.locator('[name="accessKey"]').fill(accessKey);
  await page.getByRole("button",{name:"Sign in"}).click();
  await expect(page.locator("#dashboard")).toBeVisible();
  await expect(page.locator('[name="accessKey"]')).toHaveValue("");
  expect(page.url()).not.toContain(accessKey);
  await expect(page.locator(`#workspace option[value="${knownWorkspace}"]`)).toHaveCount(1);
  if(process.env.FSGG_BROWSER_ASSERT_UNKNOWN_COUNTS==="1"){
    const unknown=page.getByRole("listitem").filter({hasText:"unknown-item"});
    const zero=page.getByRole("listitem").filter({hasText:"zero-item"});
    await expect(unknown.locator(".metric")).toHaveText("unknown tokens · unknown/unknown terminal");
    await expect(zero.locator(".metric")).toHaveText("0 tokens · 0/0 terminal");
  }
  const cookies=await context.cookies(baseURL);
  const session=cookies.find(cookie=>cookie.name==="__Host-fsgg_session");
  expect(session).toMatchObject({secure:true,httpOnly:true,sameSite:"Strict",path:"/"});

  const outcomes=await page.evaluate(async({knownWorkspace,unavailableWorkspace})=>{
    const send=body=>fetch("/private/dashboard/v1/snapshot",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(body)}).then(async response=>({status:response.status,body:await response.text()}));
    return {outside:await send({workspaceId:unavailableWorkspace}),missing:await send({workspaceId:knownWorkspace,itemId:"missing"})};
  },{knownWorkspace,unavailableWorkspace});
  expect(outcomes.outside.status).toBe(404);
  expect(outcomes.missing.status).toBe(404);
  expect(outcomes.outside.body).toBe(outcomes.missing.body);
  expect(posts.length).toBeGreaterThan(1);
  for(const request of posts)expect(request.url.startsWith(baseURL)).toBe(true);

  await page.getByRole("button",{name:"Sign out"}).click();
  await expect(page.locator("#login")).toBeVisible();
  expect((await context.cookies(baseURL)).some(cookie=>cookie.name==="__Host-fsgg_session")).toBe(false);
});
