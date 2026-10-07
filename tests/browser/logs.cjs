const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const net = require('node:net');
const { spawn } = require('node:child_process');
const { chromium } = require('playwright');
const { createFixture } = require('./fixture.cjs');

// Isolated UI contract tests never connect to the installation or real Sonos players.
(async () => {
    const root = path.resolve(__dirname, '../..');
    const output = path.join(root, 'artifacts/test-results/browser-logs');
    fs.mkdirSync(output, { recursive: true });
    const fixture = await createFixture();
    const reservation = net.createServer();
    await new Promise(resolve => reservation.listen(0, '127.0.0.1', resolve));
    const port = reservation.address().port;
    await new Promise(resolve => reservation.close(resolve));
    const base = `http://127.0.0.1:${port}`;
    const contentRoot = path.join(root, 'src/SonosMonitoring.Web');
    const environment = { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production', Api__BaseUrl: fixture.url, ApplicationLogs__Enabled: 'false' };
    delete environment.Web__AccessKey;
    delete environment.Api__ApiKey;
    const web = spawn('dotnet', [path.join(contentRoot, 'bin/Release/net10.0/SonosMonitoring.Web.dll'), '--contentRoot', contentRoot, '--urls', base], {
        cwd: root, windowsHide: true, env: environment, stdio: ['ignore', 'pipe', 'pipe']
    });
    let log = '';
    web.stdout.on('data', chunk => { log += chunk; });
    web.stderr.on('data', chunk => { log += chunk; });
    let browser;
    try {
        for (let attempt = 0; attempt < 100; attempt++) {
            if (web.exitCode !== null) throw new Error(`Web host exited: ${log}`);
            try { if ((await fetch(base, { signal: AbortSignal.timeout(2000) })).ok) break; } catch {}
            if (attempt === 99) throw new Error(`Web host not ready: ${log.slice(-3000)}`);
            await new Promise(resolve => setTimeout(resolve, 200));
        }
        browser = await chromium.launch({ headless: true, channel: process.env.PLAYWRIGHT_CHANNEL || undefined });
        const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'de-DE', timezoneId: 'Europe/Berlin' });
        const page = await context.newPage();
        const failures = [];
        page.on('pageerror', error => failures.push(error.message));
        await page.goto(base + '/logs');
        await page.locator('.log-entry').nth(2).waitFor();
        await page.locator('[data-time-zone="Europe/Berlin"]').waitFor();
        assert.match(await page.locator('.log-entry time').first().innerText(), /12:20:30 UTC\+02:00/);
        assert.equal(await page.evaluate(() => window.logInjected), undefined);
        assert.match(await page.locator('.log-message').first().innerText(), /<script>/);
        await page.screenshot({ path: path.join(output, 'logs-desktop-de.png'), fullPage: true });
        await page.getByLabel('Dienst', { exact: true }).selectOption('api');
        await page.getByLabel('Mindestschweregrad').selectOption('Warning');
        await page.getByLabel('Logs durchsuchen').fill('Docker');
        await page.getByRole('button', { name: 'Filter anwenden' }).click();
        await page.waitForFunction(() => document.querySelectorAll('.log-entry').length === 1);
        assert.match(fixture.state.logRequests.at(-1), /source=api.*minimumLevel=Warning.*search=Docker/);
        fixture.state.logsFailure = true;
        await page.getByRole('button', { name: 'Aktualisieren', exact: true }).click();
        await page.getByRole('alert').waitFor();
        assert.equal(await page.locator('.log-entry').count(), 1);
        fixture.state.logsFailure = false;
        await page.getByLabel('Logs durchsuchen').fill('absent-entry');
        await page.getByRole('button', { name: 'Filter anwenden' }).click();
        await page.getByRole('heading', { name: 'Keine passenden Logeinträge' }).waitFor();
        await page.getByLabel('Alle 10 Sekunden aktualisieren').uncheck();
        const pausedCount = fixture.state.logRequests.length;
        await page.waitForTimeout(11000);
        assert.equal(fixture.state.logRequests.length, pausedCount);
        await page.getByLabel('Logs durchsuchen').fill('');
        await page.getByRole('button', { name: 'Filter anwenden' }).click();
        await page.locator('.log-entry').waitFor();
        await page.setViewportSize({ width: 360, height: 800 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
        await page.screenshot({ path: path.join(output, 'logs-mobile-de.png'), fullPage: true });
        await page.getByRole('button', { name: 'English', exact: true }).click();
        await page.getByRole('heading', { name: 'Application logs', exact: true }).waitFor();
        await page.locator('.log-entry').first().waitFor();
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
        await page.screenshot({ path: path.join(output, 'logs-mobile-en.png'), fullPage: true });
        assert.deepEqual(failures, []);
        fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ passed: ['browser-zone timestamp', 'HTML escaping', 'filters', 'stale failure', 'empty result', 'pause refresh', '360/1440 responsive DE/EN'], browser: await browser.version() }, null, 2));
        console.log('PASS: application log UI scenarios');
    } finally {
        fs.writeFileSync(path.join(output, 'web-host.log'), log);
        if (browser) await browser.close();
        web.kill();
        fixture.server.closeAllConnections();
        await new Promise(resolve => fixture.server.close(resolve));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
