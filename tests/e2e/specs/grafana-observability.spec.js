const { test, expect } = require("@playwright/test");

const API_BASE_URL = process.env.API_BASE_URL || "http://localhost:5000";
const PROM_BASE_URL = process.env.PROMETHEUS_BASE_URL || "http://localhost:9090";
const GRAFANA_BASE_URL = process.env.GRAFANA_BASE_URL || "http://localhost:3000";
const GRAFANA_USER = process.env.GRAFANA_USER || "admin";
const GRAFANA_PASSWORD = process.env.GRAFANA_PASSWORD || "admin";

const DASHBOARDS = [
  {
    uid: "gateway-overview",
    panelTitles: ["Pipeline Throughput", "MQTT Ingest Rate", "Kafka Consumer Lag", "Pipeline Processing Duration (P95)"]
  },
  {
    uid: "pipeline-backpressure",
    panelTitles: ["MQTT Ingest Rate", "Processing Duration (P95/P99)", "Stage Throughput"]
  },
  {
    uid: "signalr-realtime",
    panelTitles: ["Messages Sent Rate", "Send Latency (P95/P99)", "Total Messages Sent"]
  }
];

async function waitForHttpOk(request, url, timeoutMs = 120000, intervalMs = 2000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    try {
      const response = await request.get(url);
      if (response.ok()) {
        return;
      }
    } catch {
      // service not ready yet
    }
    await new Promise((resolve) => setTimeout(resolve, intervalMs));
  }

  throw new Error(`Service did not become healthy: ${url}`);
}

async function seedObservabilityMetrics(request) {
  const response = await request.post(`${API_BASE_URL}/test/observability/seed`);
  expect(response.ok()).toBeTruthy();
}

async function getDashboardUrl(page, uid) {
  const response = await page.request.get(`${GRAFANA_BASE_URL}/api/dashboards/uid/${uid}`);
  if (!response.ok()) {
    throw new Error(`Failed to load dashboard UID '${uid}' from Grafana API: ${response.status()}`);
  }

  const payload = await response.json();
  const relativeUrl = payload?.meta?.url;
  if (!relativeUrl) {
    throw new Error(`Dashboard UID '${uid}' has no meta.url`);
  }

  return `${GRAFANA_BASE_URL}${relativeUrl}`;
}

async function loginGrafana(page) {
  await page.goto("/");
  await page.waitForLoadState("domcontentloaded");

  // Anonymous access can skip login page entirely.
  if (!/\/login/.test(page.url())) {
    return;
  }

  // Use session API login for stability across UI changes.
  const loginResponse = await page.request.post(`${GRAFANA_BASE_URL}/login`, {
    form: {
      user: GRAFANA_USER,
      password: GRAFANA_PASSWORD
    }
  });

  if (!loginResponse.ok()) {
    throw new Error(`Grafana login API failed: ${loginResponse.status()}`);
  }

  await page.goto("/");
  await expect(page).not.toHaveURL(/\/login/);
}

test("core Grafana observability panels should not show No data", async ({ page, request }) => {
  await waitForHttpOk(request, `${API_BASE_URL}/metrics`);
  await waitForHttpOk(request, `${PROM_BASE_URL}/-/healthy`);
  await waitForHttpOk(request, `${GRAFANA_BASE_URL}/api/health`);

  // Seed twice so rate() panels have enough samples in range.
  await seedObservabilityMetrics(request);
  await new Promise((resolve) => setTimeout(resolve, 12000));
  await seedObservabilityMetrics(request);

  await loginGrafana(page);

  for (const dashboard of DASHBOARDS) {
    const dashboardUrl = await getDashboardUrl(page, dashboard.uid);
    await page.goto(dashboardUrl);
    await page.waitForLoadState("networkidle");
    await page.waitForTimeout(5000);

    for (const title of dashboard.panelTitles) {
      const heading = page.getByRole("heading", { name: title, exact: true });
      await expect(heading).toBeVisible();
    }

    const dashboardMain = page.locator("main");
    await expect(dashboardMain).not.toContainText(/No data/i);
    await expect(dashboardMain).not.toContainText(/Query error/i);
    await expect(dashboardMain).not.toContainText(/Data is missing/i);
  }
});
