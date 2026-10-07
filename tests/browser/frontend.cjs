const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const net = require('node:net');
const { spawn } = require('node:child_process');
const { chromium } = require('playwright');
const { createFixture } = require('./fixture.cjs');

// Run against isolated loopback listeners, never the user's running API or Sonos network.
(async () => {
    const root = path.resolve(__dirname, '../..');
    const output = path.join(root, 'artifacts/test-results/browser');
    fs.mkdirSync(output, { recursive: true });
    const fixture = await createFixture();
    const reservation = net.createServer();
    await new Promise(resolve => reservation.listen(0, '127.0.0.1', resolve));
    const port = reservation.address().port;
    await new Promise(resolve => reservation.close(resolve));
    const base = `http://127.0.0.1:${port}`;
    const environment = { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production', Api__BaseUrl: fixture.url, ApplicationLogs__Enabled: 'false' };
    delete environment.Web__AccessKey;
    delete environment.Api__ApiKey;
    const contentRoot = process.env.TEST_WEB_PUBLISHED ? path.join(root, 'artifacts/publish/web') : path.join(root, 'src/SonosMonitoring.Web');
    const assembly = process.env.TEST_WEB_PUBLISHED ? path.join(contentRoot, 'SonosMonitoring.Web.dll') : path.join(contentRoot, 'bin/Release/net10.0/SonosMonitoring.Web.dll');
    const web = spawn('dotnet', [assembly, '--contentRoot', contentRoot, '--urls', base], {
        cwd: root, windowsHide: true,
        env: environment,
        stdio: ['ignore', 'pipe', 'pipe']
    });
    let log = '';
    const recordLog = chunk => { log += chunk; fs.writeFileSync(path.join(output, 'web-host.log'), log); };
    web.stdout.on('data', recordLog);
    web.stderr.on('data', recordLog);
    let browser;
    const checks = [];
    try {
        for (let attempt = 0; attempt < 100; attempt++) {
            if (web.exitCode !== null) throw new Error(`Web host exited: ${log}`);
            try { if ((await fetch(base, { signal: AbortSignal.timeout(2000) })).ok) break; } catch {}
            if (attempt === 99) throw new Error(`Web host not ready: ${log.slice(-5000)}`);
            await new Promise(resolve => setTimeout(resolve, 200));
        }
        browser = await chromium.launch({ headless: true, channel: process.env.PLAYWRIGHT_CHANNEL || undefined });
        const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, locale: 'de-DE', timezoneId: 'Europe/Berlin' });
        const page = await context.newPage();
        const failures = [];
        page.on('pageerror', error => failures.push(error.message));
        await page.goto(base);
        await page.getByRole('heading', { name: 'Dein System im Blick.' }).waitFor();
        await page.locator('.device-card').first().waitFor();
        await page.locator('[data-time-zone="Europe/Berlin"]').waitFor();
        assert.match(await page.locator('.stat-time').first().innerText(), /UTC\+[012]\d:00/);
        await page.screenshot({ path: path.join(output, 'overview-desktop-de.png'), fullPage: true });
        checks.push('desktop overview and real interactive server hydration');

        await page.goto(base + '/devices');
        await page.getByLabel('Ger\u00e4te suchen').fill('Kitchen');
        await page.waitForFunction(() => document.querySelectorAll('.device-card').length === 1);
        assert.match(await page.locator('.device-card').innerText(), /Kitchen/);
        await page.getByLabel('Ger\u00e4te suchen').fill('no-device-match');
        await page.getByRole('heading', { name: 'Keine passenden Eintr\u00e4ge' }).waitFor();
        checks.push('device search and empty result');

        await page.goto(base + '/devices/RINCON_TEST_0');
        await page.getByRole('button', { name: '\u00dcberwachung aktivieren' }).waitFor();
        await page.getByLabel('Intervall (Sekunden)').fill('37');
        await page.getByRole('button', { name: '\u00dcberwachung aktivieren' }).click();
        await page.getByRole('button', { name: 'Dieselbe Aktion wiederholen' }).waitFor();
        await page.getByRole('button', { name: 'Dieselbe Aktion wiederholen' }).click();
        await page.getByText('Gespeichert und in der Leseansicht sichtbar.').waitFor();
        assert.deepEqual(fixture.state.requests[0], fixture.state.requests[1]);
        assert.equal(JSON.parse(fixture.state.requests[0].body).intervalSeconds, 37);
        checks.push('ambiguous command retry keeps original id/body and waits for projection');
        fixture.state.conflictOnce = true;
        await page.getByRole('button', { name: '\u00dcberwachung deaktivieren' }).click();
        await page.getByRole('button', { name: 'Best\u00e4tigen', exact: true }).click();
        await page.getByText('Die Regel wurde inzwischen ge\u00e4ndert.', { exact: false }).waitFor();
        await page.getByRole('button', { name: 'Neu laden', exact: true }).click();
        await page.getByRole('button', { name: '\u00dcberwachung deaktivieren' }).click();
        await page.getByRole('button', { name: 'Best\u00e4tigen', exact: true }).click();
        await page.getByText('Gespeichert und in der Leseansicht sichtbar.').waitFor();
        assert.equal(fixture.state.policy.enabled, false);
        assert.equal(JSON.parse(fixture.state.requests.at(-1).body).expectedToken, 'opaque-token-1');
        checks.push('conflict requires reload; deactivation uses read token');

        await page.goto(base + '/groups');
        await page.locator('.group-map canvas').first().waitFor();
        await page.screenshot({ path: path.join(output, 'topology-desktop-de.png'), fullPage: true });
        const initialZoom = await page.locator('.group-map').evaluate(element => element._cyreg.cy.zoom());
        await page.getByRole('button', { name: 'Vergr\u00f6\u00dfern' }).click();
        await page.waitForFunction(zoom => document.querySelector('.group-map')._cyreg.cy.zoom() > zoom, initialZoom);
        const zoomBeforeRefresh = await page.locator('.group-map').evaluate(element => element._cyreg.cy.zoom());
        const graph = await page.locator('.group-map').evaluate(element => {
            const cy = element._cyreg.cy;
            return { nodes: cy.nodes('[deviceId]').length, edges: cy.edges().length, coordinator: cy.nodes('[kind="coordinator"]').length,
                satellites: cy.edges('[kind="satellite"]').length, references: cy.nodes('[quality="reference"]').length };
        });
        assert.deepEqual(graph, { nodes: 4, edges: 3, coordinator: 1, satellites: 1, references: 1 });
        await page.locator('.group-map').scrollIntoViewIfNeeded();
        const position = await page.locator('.group-map').evaluate(element => {
            const position = element._cyreg.cy.nodes('[kind="coordinator"]').renderedPosition();
            const bounds = element.getBoundingClientRect();
            return { x: bounds.x + position.x, y: bounds.y + position.y };
        });
        await page.mouse.click(position.x, position.y);
        await page.getByRole('heading', { name: 'Living room', exact: true }).waitFor();
        await page.mouse.move(position.x, position.y);
        await page.mouse.down();
        await page.mouse.move(position.x + 40, position.y + 15, { steps: 8 });
        await page.mouse.up();
        const movedX = await page.locator('.group-map').evaluate(element => element._cyreg.cy.nodes('[kind="coordinator"]').position().x);
        await page.getByRole('button', { name: 'Aktualisieren', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('.loading') === null);
        assert.equal(await page.locator('.group-map').evaluate(element => element._cyreg.cy.nodes('[kind="coordinator"]').position().x), movedX);
        await page.locator('.group-map').focus();
        const beforePan = await page.locator('.group-map').evaluate(element => element._cyreg.cy.pan().x);
        await page.keyboard.press('ArrowRight');
        assert.equal(await page.locator('.group-map').evaluate(element => element._cyreg.cy.pan().x), beforePan - 40);
        await page.getByLabel('Ger\u00e4t in der Karte ausw\u00e4hlen').selectOption('RINCON_TEST_1');
        await page.getByRole('heading', { name: 'Kitchen', exact: true }).waitFor();
        await page.locator('section[aria-live="polite"] .device-live-summary').getByText('\u00dcberwachung deaktiviert', { exact: false }).first().waitFor();
        for (const [code, label] of [['policy_unavailable', '\u00dcberwachungsregel nicht verf\u00fcgbar'], ['diagnostics_not_due', 'N\u00e4chste Diagnose noch nicht f\u00e4llig'], ['not_supported', 'Nicht unterst\u00fctzt']]) {
            fixture.document.snapshot.devices[1].interfaceStatus.errorCode = code;
            await page.getByRole('button', { name: 'Aktualisieren', exact: true }).click();
            await page.locator('section[aria-live="polite"] .device-live-summary').getByText(label, { exact: false }).first().waitFor();
        }
        fixture.document.snapshot.devices[1].interfaceStatus.errorCode = 'monitoring_disabled';
        fixture.document.snapshot.devices[0].interfaceRates[1].receiveBytesPerSecond = null;
        await page.getByRole('button', { name: 'Aktualisieren', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('.group-map')._cyreg.cy.nodes('[kind="coordinator"]').data('label').includes('RX Unbekannt'));
        fixture.document.snapshot.devices[0].interfaceRates[1].receiveBytesPerSecond = 1000;
        await page.getByRole('button', { name: 'Liste', exact: true }).click();
        await page.locator('.member-list').waitFor();
        checks.push('rendered topology nodes/edges, zoom, keyboard pan, device selection and equivalent list');

        await page.goto(base + '/settings');
        const ssdpHelp = page.locator('[data-protocol="ssdp"]');
        const genaHelp = page.locator('[data-protocol="gena"]');
        await ssdpHelp.locator('summary').waitFor();
        assert.match(await ssdpHelp.innerText(), /Simple Service Discovery Protocol/);
        assert.match(await genaHelp.innerText(), /General Event Notification Architecture/);
        await ssdpHelp.locator('summary').focus();
        await page.keyboard.press('Enter');
        assert.match(await ssdpHelp.innerText(), /TTL 1/);
        assert.match(await ssdpHelp.innerText(), /239\.255\.255\.250:1900/);
        await genaHelp.locator('summary').click();
        assert.match(await genaHelp.innerText(), /ZoneGroupTopology/);
        assert.match(await genaHelp.innerText(), /48081/);
        assert.match(await genaHelp.innerText(), /Initialmeldung empfangen/);
        await ssdpHelp.locator('summary').click();
        await genaHelp.locator('summary').click();
        checks.push('German SSDP/GENA explanations, keyboard-accessible setup, topology-only event scope and callback verification');
        await page.getByLabel('Erlaubte IPv4-Netze', { exact: false }).fill('192.0.2.0/24');
        await page.getByLabel('Startadressen', { exact: false }).fill('192.0.2.5');
        await page.getByLabel('Abfrageintervall (Sekunden)', { exact: true }).fill('20');
        await page.getByLabel('Erfassung aktiv', { exact: true }).check();
        await page.screenshot({ path: path.join(output, 'settings-desktop-de.png'), fullPage: true });
        fixture.state.settingsAmbiguous = true;
        await page.getByRole('button', { name: 'Speichern und anwenden', exact: true }).click();
        await page.getByRole('button', { name: 'Speichern erneut best\u00e4tigen', exact: true }).click();
        await page.getByText('Einstellungen gespeichert und \u00fcbernommen.', { exact: true }).waitFor();
        assert.deepEqual(fixture.state.settingsRequests[0], fixture.state.settingsRequests[1]);
        assert.equal(fixture.state.configuration.settings.pollInterval, '00:00:20');
        assert.equal(fixture.state.configuration.settings.enabled, true);
        assert.equal(fixture.state.configuration.settings.ssdpEnabled, false);
        assert.equal(fixture.state.configuration.settings.gena.enabled, false);
        await page.reload();
        await page.waitForFunction(() => document.querySelector('textarea')?.value === '192.0.2.0/24');
        fixture.state.settingsInvalid = true;
        await page.getByRole('button', { name: 'Speichern und anwenden', exact: true }).click();
        await page.getByText('Ung\u00fcltige Erfassungseinstellungen.', { exact: false }).waitFor();
        fixture.state.settingsConflict = true;
        await page.getByRole('button', { name: 'Speichern und anwenden', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('fieldset').disabled);
        await page.getByRole('button', { name: 'Neu laden', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('fieldset')?.disabled === false);
        checks.push('settings form saves all fields, reloads persisted values, retries identical requests, reports validation and requires reload on conflict');

        await page.goto(base + '/metrics');
        await page.locator('.metric-chart path').waitFor();
        assert.equal((await page.locator('.metric-chart path').getAttribute('d')).split('M').length - 1, 2);
        await page.locator('summary').click();
        await page.getByRole('cell', { name: 'Messl\u00fccke', exact: true }).waitFor();
        await page.screenshot({ path: path.join(output, 'metrics-desktop-de.png'), fullPage: true });
        assert.equal(await page.locator('.filter-bar select').nth(1).locator('option').count(), 26);
        await page.getByLabel('Metrik', { exact: true }).selectOption('receive_dropped_rate');
        await page.getByRole('button', { name: 'Abfragen', exact: true }).click();
        await page.locator('.chart-panel').filter({ hasText: 'drops/second' }).waitFor();
        await page.getByLabel('Metrik', { exact: true }).selectOption('source_age');
        await page.getByLabel('Quelle', { exact: true }).selectOption('topology');
        await page.getByRole('button', { name: 'Abfragen', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('.loading') === null);
        assert.equal(fixture.state.metricRequests.at(-1).sourceFilter, 'topology');
        fixture.state.metricEmpty = true;
        await page.getByLabel('Ger\u00e4te', { exact: true }).selectOption('RINCON_TEST_1');
        await page.getByRole('button', { name: 'Abfragen', exact: true }).click();
        await page.getByRole('heading', { name: 'Keine Messwerte im Zeitraum', exact: true }).waitFor();
        await page.locator('main .monitoring-link').first().waitFor();
        fixture.state.metricEmpty = false;
        await page.getByLabel('Ger\u00e4te', { exact: true }).selectOption('RINCON_TEST_0');
        await page.getByLabel('Metrik', { exact: true }).selectOption('receive_rate');
        checks.push('complete API catalog, counter rate units, source filter, explained disabled empty state and monitoring action link');
        await page.getByLabel('Zeitraum').selectOption('0');
        await page.getByLabel('Beginn (Europe/Berlin)', { exact: true }).fill('2026-09-01T00:00');
        await page.getByLabel('Ende (Europe/Berlin)', { exact: true }).fill('2026-09-02T00:00');
        await page.getByRole('button', { name: 'Abfragen', exact: true }).click();
        await page.locator('.chart-panel').filter({ hasText: '01.09.2026' }).waitFor();
        assert.equal(Date.parse(fixture.state.metricRequests.at(-1).start), Date.parse('2026-08-31T22:00:00Z'));
        assert.equal(Date.parse(fixture.state.metricRequests.at(-1).end), Date.parse('2026-09-01T22:00:00Z'));
        assert.match(await page.locator('.chart-panel > p').first().innerText(), /UTC\+02:00/);
        checks.push('browser-local custom range converted to UTC and displayed with its actual offset');
        await page.getByLabel('Beginn (Europe/Berlin)', { exact: true }).fill('2026-10-25T02:30');
        await page.getByLabel('Ende (Europe/Berlin)', { exact: true }).fill('2026-10-25T04:00');
        const requestsBeforeInvalidTime = fixture.state.metricRequests.length;
        await page.getByRole('button', { name: 'Abfragen', exact: true }).click();
        await page.getByRole('alert').filter({ hasText: 'Zeitumstellung' }).waitFor();
        assert.equal(fixture.state.metricRequests.length, requestsBeforeInvalidTime);
        checks.push('ambiguous DST input is explained and never silently queried');

        for (const [zone, offset, utcHour] of [['America/New_York', '-05:00', '17'], ['Asia/Kolkata', '+05:30', '06']]) {
            const zoneContext = await browser.newContext({ locale: 'en-US', timezoneId: zone });
            const zonePage = await zoneContext.newPage();
            await zonePage.goto(base + '/metrics');
            await zonePage.getByRole('button', { name: 'English', exact: true }).click();
            await zonePage.waitForFunction(() => document.documentElement.lang === 'en');
            const browserZone = await zonePage.evaluate(() => Intl.DateTimeFormat().resolvedOptions().timeZone);
            await zonePage.locator(`[data-time-zone="${browserZone}"]`).waitFor();
            await zonePage.locator('.metric-chart path').waitFor();
            await zonePage.getByRole('combobox', { name: /^Period/ }).selectOption('0');
            await zonePage.locator('input[type="datetime-local"]').nth(0).fill('2026-01-15T12:00');
            await zonePage.locator('input[type="datetime-local"]').nth(1).fill('2026-01-15T13:00');
            await zonePage.getByRole('button', { name: 'Query', exact: true }).click();
            await zonePage.locator('.chart-panel > p').first().filter({ hasText: `UTC${offset}` }).filter({ hasText: '1/15/2026' }).waitFor();
            const minute = zone === 'Asia/Kolkata' ? '30' : '00';
            assert.equal(Date.parse(fixture.state.metricRequests.at(-1).start), Date.parse(`2026-01-15T${utcHour}:${minute}:00Z`));
            await zoneContext.close();
        }
        assert.equal(await page.locator('[data-time-zone]').getAttribute('data-time-zone'), 'Europe/Berlin');
        const fallbackContext = await browser.newContext({ locale: 'en-US', timezoneId: 'America/New_York' });
        await fallbackContext.addInitScript(() => {
            const resolvedOptions = Intl.DateTimeFormat.prototype.resolvedOptions;
            Intl.DateTimeFormat.prototype.resolvedOptions = function () { return { ...resolvedOptions.call(this), timeZone: undefined }; };
        });
        const fallbackPage = await fallbackContext.newPage();
        await fallbackPage.goto(base);
        await fallbackPage.getByRole('button', { name: 'English', exact: true }).click();
        await fallbackPage.waitForFunction(() => document.documentElement.lang === 'en');
        await fallbackPage.locator('.device-card').first().waitFor();
        await fallbackPage.locator('[data-time-zone="UTC"]').filter({ hasText: 'unavailable' }).waitFor();
        assert.match(await fallbackPage.locator('.stat-time').first().innerText(), /UTC\+00:00/);
        await fallbackContext.close();
        checks.push('independent browser zones, historical winter offset, half-hour offset and explicit UTC fallback');

        await page.goto(base + '/system');
        await page.locator('.collector-diagnostics').getByRole('heading', { name: 'Erfassungsdiagnose', exact: true }).waitFor();
        await page.locator('.collector-diagnostics').getByText('20', { exact: true }).waitFor();
        await page.locator('.collector-diagnostics').getByText('0,45 Sekunden', { exact: true }).waitFor();
        await page.screenshot({ path: path.join(output, 'collector-desktop-de.png'), fullPage: true });
        checks.push('collector duration, probe counters and deduplicated discovery totals are visible');

        await page.goto(base + '/history');
        await page.getByRole('heading', { name: 'Erfassungsl\u00fccke', exact: true }).waitFor();
        await page.getByText('\u00c4ltere Eintr\u00e4ge wurden bereits entfernt.', { exact: false }).waitFor();
        await page.getByRole('button', { name: 'Weitere laden' }).click();
        checks.push('history paging and explicit retention gap');

        await page.goto(base + '/alerts');
        await page.locator('.alert-card.recovered').waitFor();
        assert.equal(await page.locator('.alert-card').count(), 3);
        fixture.state.alertsFailure = true;
        await page.getByRole('button', { name: 'Aktualisieren', exact: true }).click();
        await page.getByRole('alert').filter({ hasText: 'Die Daten konnten nicht abgerufen' }).waitFor();
        assert.equal(await page.locator('.alert-card').count(), 3);
        fixture.state.alertsFailure = false;
        checks.push('explicit recovery and failed refresh retains marked last data');

        await page.goto(base + '/snapshots');
        const downloaded = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Snapshot herunterladen', exact: true }).click();
        const download = await downloaded;
        assert.equal(download.suggestedFilename(), 'sonosmonitoring-snapshot.json');
        await download.saveAs(path.join(output, 'downloaded-snapshot.json'));
        assert.equal(JSON.parse(fs.readFileSync(path.join(output, 'downloaded-snapshot.json'), 'utf8')).schemaVersion, 1);
        await page.locator('input[type=file]').setInputFiles({ name: 'invalid.json', mimeType: 'application/json', buffer: Buffer.from('{"snapshot":null}') });
        await page.getByText('Die Datei ist kein unterst\u00fctzter Snapshot', { exact: false }).waitFor();
        await page.locator('input[type=file]').setInputFiles({ name: 'snapshot.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(fixture.document)) });
        await page.getByRole('button', { name: 'Offlineansicht schlie\u00dfen' }).waitFor();
        assert.equal(await page.getByRole('button', { name: '\u00dcberwachung aktivieren' }).count(), 0);
        checks.push('invalid and valid offline snapshot; no write actions');

        for (const language of ['de', 'en']) {
            await page.getByRole('button', { name: language === 'en' ? 'English' : 'Deutsch', exact: true }).click();
            await page.waitForFunction(lang => document.documentElement.lang === lang, language);
            await page.setViewportSize({ width: 360, height: 800 });
            for (const route of ['/', '/devices', '/devices/RINCON_TEST_0', '/groups', '/metrics', '/history', '/alerts', '/logs', '/snapshots', '/system', '/settings']) {
                await page.goto(base + route);
                await page.locator('main h1').waitFor();
                await page.waitForFunction(() => document.querySelector('.loading') === null);
                assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `Overflow at ${route} (${language})`);
                assert.equal(await page.locator('#blazor-error-ui').isVisible(), false, `Blazor error at ${route}`);
                if (route === '/settings') {
                    for (const protocol of ['ssdp', 'gena']) {
                        const help = page.locator(`[data-protocol="${protocol}"]`);
                        await help.locator('summary').click();
                        assert.match(await help.innerText(), protocol === 'ssdp' ? /TTL 1/ : /ZoneGroupTopology/);
                    }
                    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `Expanded protocol help overflow (${language})`);
                    assert.match(await page.locator('[data-protocol="gena"]').innerText(), language === 'de' ? /R\u00fcckrufadresse/ : /callback address/);
                    await page.screenshot({ path: path.join(output, `settings-help-mobile-${language}.png`), fullPage: true });
                }
            }
            await page.goto(base);
            await page.screenshot({ path: path.join(output, `overview-mobile-${language}.png`), fullPage: true });
            checks.push(`360px all routes without horizontal page overflow (${language})`);
        }
        await page.reload();
        assert.equal(await page.locator('html').getAttribute('lang'), 'en');
        await page.keyboard.press('Tab');
        assert.match(await page.locator(':focus').innerText(), /Skip to content/);
        checks.push('persistent language preference and keyboard skip navigation');
        const touchContext = await browser.newContext({ viewport: { width: 360, height: 800 }, hasTouch: true, isMobile: true, locale: 'en-US' });
        const touchPage = await touchContext.newPage();
        await touchPage.goto(base + '/groups');
        await touchPage.locator('.group-map canvas').first().waitFor();
        await touchPage.screenshot({ path: path.join(output, 'topology-mobile-en.png'), fullPage: true });
        await touchPage.getByRole('button', { name: 'Zoom in', exact: true }).tap();
        await touchPage.getByRole('button', { name: 'List', exact: true }).tap();
        await touchPage.locator('.member-list').waitFor();
        await touchPage.locator('.member-list a').first().tap();
        await touchPage.getByRole('heading', { name: 'Monitoring policy', exact: true }).waitFor();
        await touchContext.close();
        checks.push('touch group selection and device detail navigation');
        assert.deepEqual(failures, []);
        fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ checks, browser: await browser.version() }, null, 2));
        console.log(`PASS: ${checks.length} browser scenarios`);
    } finally {
        fs.writeFileSync(path.join(output, 'web-host.log'), log);
        if (browser) await browser.close();
        web.kill();
        fixture.server.closeAllConnections();
        await new Promise(resolve => fixture.server.close(resolve));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
