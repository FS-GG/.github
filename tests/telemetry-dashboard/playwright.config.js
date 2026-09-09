module.exports = {
  testDir: __dirname,
  timeout: 30000,
  use: { baseURL: "http://127.0.0.1:8473", browserName: "chromium" },
  reporter: "line",
};
