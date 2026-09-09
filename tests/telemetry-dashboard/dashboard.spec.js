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
    schema: "fsgg.telemetry.dashboard/1",
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
        schema: "fsgg.telemetry.dashboard/1",
        actions: { schema: "bad", runs: [] },
      },
    }),
  );
  await page.goto("/");
  await expect(page.locator("#error")).toBeVisible();
  await expect(page.locator("#health-label")).toHaveText("Data unavailable");
});
