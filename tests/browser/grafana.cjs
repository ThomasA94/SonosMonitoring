const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

// Read-only acceptance against the explicitly configured local Grafana; no LAN probes or setting changes.
(async () => {
    const base = process.env.GRAFANA_TEST_URL || 'http://127.0.0.1:48300';
    const url = new URL(base);
    assert.ok(['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname), 'Grafana acceptance must use loopback.');
    assert.ok(process.env.GRAFANA_ADMIN_USER && process.env.GRAFANA_ADMIN_PASSWORD, 'Set Grafana test credentials in the process environment.');
    const root = path.resolve(__dirname, '../..');
    const directory = path.join(root, 'infra/grafana/dashboards');
    const dashboards = fs.readdirSync(directory).filter(name => name.endsWith('.json'))
        .map(name => JSON.parse(fs.readFileSync(path.join(directory, name), 'utf8')));
    const output = path.join(root, 'artifacts/test-results/grafana-browser');
    fs.mkdirSync(output, { recursive: true });
    const checks = [];
    const browser = await chromium.launch({ headless: true, channel: process.env.PLAYWRIGHT_CHANNEL || undefined });
    try {
        const context = await browser.newContext({ viewport: { width: 1440, height: 1050 } });
        const page = await context.newPage();
        await page.goto(`${base}/login`);
        await page.getByLabel('Email or username').fill(process.env.GRAFANA_ADMIN_USER);
        await page.getByLabel('Password', { exact: true }).fill(process.env.GRAFANA_ADMIN_PASSWORD);
        await page.getByRole('button', { name: 'Log in', exact: true }).click();
        await page.waitForURL(address => address.pathname !== '/login');
        checks.push('authenticated Grafana login');

        const metadataResponse = await context.request.get(`${base}/api/datasources/proxy/uid/sonosmonitoring-prometheus/api/v1/query?query=sonos_device_info`);
        assert.equal(metadataResponse.status(), 200);
        const metadata = (await metadataResponse.json()).data.result;
        assert.ok(metadata.length > 0, 'Current player metadata must be scraped before Grafana acceptance.');
        assert.ok(metadata.every(series => series.metric.device_name && series.metric.device_name !== series.metric.device_id));
        const selected = metadata[0].metric;

        for (const dashboard of dashboards) {
            const response = await context.request.get(`${base}/api/dashboards/uid/${dashboard.uid}`);
            assert.equal(response.status(), 200, `Provisioned dashboard ${dashboard.uid} must exist.`);
            const provisioned = (await response.json()).dashboard;
            assert.equal(provisioned.panels.length, dashboard.panels.length);
            assert.deepEqual(provisioned.panels.map(panel => ({ title: panel.title, targets: (panel.targets || []).map(target => target.expr) })),
                dashboard.panels.map(panel => ({ title: panel.title, targets: (panel.targets || []).map(target => target.expr) })));
            await page.goto(`${base}/d/${dashboard.uid}?from=now-15m&to=now&var-device=${selected.device_id}`);
            await page.getByText(dashboard.title, { exact: true }).first().waitFor();
            const firstPanel = dashboard.panels.find(panel => panel.type !== 'row' && panel.type !== 'text');
            if (firstPanel) await page.getByText(firstPanel.title, { exact: true }).first().waitFor();
            await page.getByText(selected.device_name, { exact: true }).first().waitFor();
            await page.waitForLoadState('networkidle');
            await page.screenshot({ path: path.join(output, `${dashboard.uid}-desktop.png`) });
            assert.equal(await page.getByText(selected.device_id, { exact: true }).count(), 0, 'Opaque device keys must not be displayed as names.');
            checks.push(`${dashboard.uid}: current provisioning and readable device selection`);
            const chart = dashboard.panels.find(panel => panel.type === 'timeseries');
            if (chart) {
                await page.getByText(chart.title, { exact: true }).first().scrollIntoViewIfNeeded();
                await page.waitForLoadState('networkidle');
                await page.waitForFunction(() => Array.from(document.querySelectorAll('button'))
                    .some(button => button.textContent.trim() === 'Cancel') === false);
                // Off-screen Grafana panels start queries and canvas layout after becoming visible.
                await page.waitForTimeout(2000);
                await page.screenshot({ path: path.join(output, `${dashboard.uid}-chart.png`) });
                checks.push(`${dashboard.uid}: detailed chart is reachable`);
            }
        }
        if (metadata.length > 1) {
            const pair = metadata.slice(0, 2).map(series => series.metric);
            const selection = pair.map(device => `var-device=${device.device_id}`).join('&');
            await page.goto(`${base}/d/sonosmonitoring-overview?from=now-15m&to=now&${selection}&var-interface=br0&var-source=ifconfig`);
            for (const device of pair) await page.getByText(device.device_name, { exact: true }).first().waitFor();
            await page.waitForLoadState('networkidle');
            await page.screenshot({ path: path.join(output, 'overview-multiple-devices.png') });
            checks.push('multiple devices use readable names with interface and source filters');
            await page.getByRole('link', { name: 'SonosMonitoring \u00b7 Netzwerk', exact: true }).click();
            await page.getByText('SonosMonitoring \u00b7 Netzwerk', { exact: true }).first().waitFor();
            const linked = new URL(page.url());
            assert.deepEqual(linked.searchParams.getAll('var-device').sort(), pair.map(device => device.device_id).sort());
            assert.equal(linked.searchParams.get('var-interface'), 'br0');
            assert.equal(linked.searchParams.get('var-source'), 'ifconfig');
            assert.equal(linked.searchParams.get('from'), 'now-15m');
            checks.push('dashboard navigation preserves device, interface, source and time selection');
        }
        await page.setViewportSize({ width: 390, height: 844 });
        await page.goto(`${base}/d/sonosmonitoring-overview?from=now-15m&to=now&var-device=${selected.device_id}`);
        await page.getByText(selected.device_name, { exact: true }).first().waitFor();
        await page.waitForLoadState('networkidle');
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), 'Mobile Grafana must not overflow the page.');
        await page.screenshot({ path: path.join(output, 'overview-mobile.png') });
        checks.push('mobile Grafana overview with readable selection');
        fs.writeFileSync(path.join(output, 'checks.json'), JSON.stringify({ passed: checks.length, checks }, null, 2) + '\n');
        console.log(`PASS ${checks.length} Grafana browser checks.`);
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
