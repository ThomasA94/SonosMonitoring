const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const net = require('node:net');
const { spawn } = require('node:child_process');
const { chromium } = require('playwright');
const { createFixture } = require('./fixture.cjs');

// Isolated loopback API and Web host; no request can address a physical Sonos player.
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
    const environment = { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production', Api__BaseUrl: fixture.url };
    delete environment.Web__AccessKey;
    delete environment.Api__ApiKey;
    const contentRoot = process.env.TEST_WEB_PUBLISHED ? path.join(root, 'artifacts/publish/web') : path.join(root, 'src/SonosMonitoring.Web');
    const assembly = process.env.TEST_WEB_PUBLISHED ? path.join(contentRoot, 'SonosMonitoring.Web.dll') : path.join(contentRoot, 'bin/Release/net10.0/SonosMonitoring.Web.dll');
    const web = spawn('dotnet', [assembly, '--contentRoot', contentRoot, '--urls', base], {
        cwd: root, windowsHide: true, env: environment, stdio: ['ignore', 'pipe', 'pipe']
    });
    let log = '';
    const recordLog = chunk => { log += chunk; fs.writeFileSync(path.join(output, 'media-web-host.log'), log); };
    web.stdout.on('data', recordLog);
    web.stderr.on('data', recordLog);
    let browser;
    let page;
    const checks = [];
    try {
        for (let attempt = 0; attempt < 100; attempt++) {
            if (web.exitCode !== null) throw new Error(`Web host exited: ${log}`);
            try { if ((await fetch(base, { signal: AbortSignal.timeout(2000) })).ok) break; } catch {}
            if (attempt === 99) throw new Error(`Web host not ready: ${log.slice(-5000)}`);
            await new Promise(resolve => setTimeout(resolve, 200));
        }
        browser = await chromium.launch({ headless: true, channel: process.env.PLAYWRIGHT_CHANNEL || undefined });
        const context = await browser.newContext({ viewport: { width: 1440, height: 960 }, locale: 'de-DE' });
        page = await context.newPage();
        const failures = [];
        page.on('pageerror', error => failures.push(error.message));

        await page.goto(base + '/media');
        await page.getByRole('heading', { level: 1, name: 'Mediensteuerung', exact: true }).waitFor();
        await page.getByText('Mediensteuerung ist gesperrt.').first().waitFor();
        assert.equal(await page.getByRole('button', { name: 'Pause' }).isDisabled(), true);
        assert.equal(await page.getByRole('button', { name: 'Weiter' }).isDisabled(), true);
        checks.push('default-disabled gate and unavailable capability');

        const enable = page.getByRole('button', { name: 'Steuerung freigeben' });
        await enable.focus();
        await page.keyboard.press('Enter');
        await page.getByText('Mediensteuerung ist freigegeben.').first().waitFor();
        assert.equal(fixture.state.media.enabled, true);
        checks.push('keyboard activation of explicit global grant');

        await page.goto(base + '/devices/RINCON_TEST_0');
        const panel = page.locator('.media-panel');
        await panel.getByText('Fixture song').waitFor();
        assert.equal(await panel.getByRole('button', { name: 'Weiter' }).isDisabled(), true);
        const playerSlider = panel.getByRole('slider', { name: 'Lautstärke einstellen' }).first();
        await playerSlider.focus();
        await page.keyboard.press('ArrowRight');
        const chosenVolume = Number(await playerSlider.inputValue());
        assert.equal(chosenVolume, 25);
        const readsBeforeRefresh = fixture.state.media.stateReads;
        await panel.locator('.media-heading button').click();
        for (let attempt = 0; attempt < 100 && fixture.state.media.stateReads <= readsBeforeRefresh; attempt++) {
            await new Promise(resolve => setTimeout(resolve, 20));
        }
        assert.ok(fixture.state.media.stateReads > readsBeforeRefresh);
        await page.waitForFunction(() => document.querySelector('.media-panel .media-heading button')?.disabled === false);
        assert.equal(Number(await playerSlider.inputValue()), chosenVolume);
        await panel.getByRole('button', { name: 'Lautstärke übernehmen' }).first().click();
        await panel.getByRole('button', { name: 'Dieselbe Anfrage wiederholen' }).waitFor();
        await panel.getByRole('button', { name: 'Dieselbe Anfrage wiederholen' }).click();
        await panel.getByText('Dauerhaft angenommen').waitFor();
        assert.deepEqual(fixture.state.media.requests[0], fixture.state.media.requests[1]);
        assert.equal(JSON.parse(fixture.state.media.requests[0].body).context.sourceToken, 'source-1');
        assert.equal(JSON.parse(fixture.state.media.requests[0].body).value, chosenVolume);
        checks.push('keyboard-selected volume survives explicit refresh and ambiguous retry preserves exact request');

        const firstId = fixture.state.media.requests[0].key;
        fixture.state.media.operations.get(firstId).status = 'outcome_unknown';
        fixture.state.media.operations.get(firstId).code = 'media.outcome.unknown';
        fixture.state.media.blocks.push({ operationId: firstId, deviceKeys: ['RINCON_TEST_0', 'RINCON_TEST_1'], action: 'volume', value: chosenVolume, status: 'outcome_unknown' });
        await panel.getByRole('button', { name: 'Vorgang prüfen' }).click();
        await panel.locator('.media-heading button').click();
        await panel.getByText('Ziel dauerhaft gesperrt').waitFor();
        await panel.locator('.media-heading button').click();
        assert.equal(await panel.getByRole('button', { name: 'Lautstärke übernehmen' }).first().isDisabled(), true);
        assert.equal(fixture.state.media.blocks.length, 1);
        checks.push('unknown outcome persists across refresh and blocks further writes');

        await panel.getByRole('link', { name: 'Risikoannahme öffnen' }).click();
        await page.getByText(firstId).first().waitFor();
        await page.getByRole('checkbox', { name: /alter Schreibvorgang/ }).check();
        await page.getByRole('button', { name: 'Benannte Vorgänge entsperren' }).click();
        await page.locator('.media-settings').getByText('Ziel dauerhaft gesperrt').waitFor({ state: 'detached' });
        assert.equal(fixture.state.media.blocks.length, 0);
        const risk = JSON.parse(fixture.state.media.riskRequests[0].body);
        assert.deepEqual(risk.operationIds, [firstId]);
        assert.equal(risk.confirmDelayedEffect, true);
        checks.push('risk acknowledgement names exact blocked operation and delayed effect');

        await page.locator('.media-panel .media-heading button').click();
        await page.locator('.media-library summary').click();
        await page.locator('.media-library').getByRole('button', { name: 'Stream starten' }).click();
        await page.getByRole('heading', { name: 'Änderung bestätigen' }).waitFor();
        await page.locator('.media-panel').getByRole('button', { name: 'Bestätigen' }).click();
        await page.locator('.media-panel .media-operation strong').getByText('Stream starten').waitFor();
        const streamRequest = fixture.state.media.requests.at(-1);
        assert.equal(streamRequest.path, '/api/media/players/RINCON_TEST_0/start-stream');
        assert.equal(JSON.parse(streamRequest.body).reference, 'favorite:opaque-1');
        fixture.state.media.operations.get(streamRequest.key).status = 'desired_state_observed';
        fixture.state.media.operations.get(streamRequest.key).code = 'media.desired_state.observed';
        await page.locator('.media-panel').getByRole('button', { name: 'Vorgang prüfen' }).click();
        checks.push('stream start binds a selected server-issued favorite reference');

        await page.getByText('Fixture song').last().waitFor();
        await page.locator('.media-library').getByRole('button', { name: 'Abspielen', exact: true }).click();
        await page.getByRole('heading', { name: 'Änderung bestätigen' }).waitFor();
        await page.locator('.media-panel').getByRole('button', { name: 'Bestätigen' }).click();
        await page.locator('.media-panel .media-operation strong').getByText('Queueeintrag spielen').waitFor();
        const queueRequest = fixture.state.media.requests.at(-1);
        assert.equal(queueRequest.path, '/api/media/players/RINCON_TEST_0/queue-play');
        assert.equal(JSON.parse(queueRequest.body).reference, 'queue:opaque-1');
        assert.equal(JSON.parse(queueRequest.body).context.queueRevision, 'queue-7');
        checks.push('queue playback binds opaque reference and displayed revision after confirmation');

        const partialOperation = fixture.state.media.operations.get(queueRequest.key);
        partialOperation.status = 'revoked';
        partialOperation.code = 'media.partial.revoked';
        partialOperation.step = 0;
        await page.locator('.media-panel .media-operation').getByText(queueRequest.key).waitFor();
        await page.locator('.media-panel .media-operation').getByRole('button', { name: 'Vorgang prüfen' }).click();
        await page.getByText('Letzter freigegebener Ausführungsschritt:', { exact: false }).waitFor();
        await page.getByText('Bereits freigegebene Schritte können wirksam bleiben.', { exact: false }).waitFor();
        checks.push('partial revocation shows the released step and warns about lasting effects');

        const groupDevices = fixture.document.snapshot.devices;
        const member = index => ({ deviceId: `RINCON_TEST_${index}`, roomName: groupDevices[index].roomName, parentDeviceId: null, isInvisible: false });
        const groups = [{ groupId: 'group-a', coordinatorDeviceId: 'RINCON_TEST_0', members: [member(0), member(1)] },
            { groupId: 'group-b', coordinatorDeviceId: 'RINCON_TEST_2', members: [member(2)] }];
        for (const device of groupDevices) { device.groups = groups; device.description.isStale = false; }
        const playback = fixture.state.media.playback;
        fixture.state.media.players = {
            RINCON_TEST_1: { ...playback, context: { ...playback.context, deviceKey: 'RINCON_TEST_1' }, availableActions: ['leave', 'resume'] },
            RINCON_TEST_2: { ...playback, context: { ...playback.context, deviceKey: 'RINCON_TEST_2', coordinatorDeviceKey: 'RINCON_TEST_2', memberDeviceKeys: ['RINCON_TEST_2'] }, availableActions: ['join', 'resume'] }
        };
        await page.goto(base + '/media?device=RINCON_TEST_0');
        const membership = page.locator('.media-membership');
        const targetPicker = page.locator('.media-picker select');
        await membership.getByRole('button', { name: 'Raum entfernen: Kitchen', exact: true }).waitFor();
        assert.equal(await membership.getByRole('button', { name: 'Raum entfernen: Living room', exact: true }).isDisabled(), true);
        const beforeGroup = fixture.state.media.requests.length;
        await membership.getByLabel('Raum hinzufügen', { exact: true }).selectOption('RINCON_TEST_2');
        await membership.getByRole('button', { name: 'Raum hinzufügen', exact: true }).click();
        const confirmation = page.getByRole('group', { name: 'Änderung bestätigen', exact: true });
        await confirmation.getByText('Betroffener Raum: Studio').waitFor();
        await page.waitForFunction(() => document.querySelector('.media-picker select').disabled);
        assert.equal(fixture.state.media.requests.length, beforeGroup);
        await confirmation.getByRole('button', { name: 'Abbrechen' }).click();
        assert.equal(fixture.state.media.requests.length, beforeGroup);
        checks.push('group membership review names the actual added room and cancellation makes no write');

        fixture.state.media.ambiguousOnce = true;
        await membership.getByRole('button', { name: 'Raum hinzufügen', exact: true }).click();
        await confirmation.getByRole('button', { name: 'Bestätigen' }).click();
        await page.getByRole('button', { name: 'Dieselbe Anfrage wiederholen' }).waitFor();
        assert.equal(await targetPicker.isDisabled(), true);
        await page.getByRole('button', { name: 'Dieselbe Anfrage wiederholen' }).click();
        await page.locator('.media-operation').getByText('Dauerhaft angenommen').waitFor();
        const joinRequest = fixture.state.media.requests.at(-1);
        assert.deepEqual(joinRequest, fixture.state.media.requests.at(-2));
        assert.equal(joinRequest.path, '/api/media/players/RINCON_TEST_2/join');
        const joinBody = JSON.parse(joinRequest.body);
        assert.equal(joinBody.reference, 'RINCON_TEST_0');
        assert.equal(joinBody.context.deviceKey, 'RINCON_TEST_2');
        assert.deepEqual(joinBody.destinationContext, playback.context);
        fixture.state.media.operations.get(joinRequest.key).status = 'desired_state_observed';
        await page.locator('.media-panel').getByRole('button', { name: 'Vorgang prüfen' }).click();
        const standaloneStudio = fixture.state.media.players.RINCON_TEST_2;
        playback.context = { ...playback.context, memberDeviceKeys: ['RINCON_TEST_0', 'RINCON_TEST_1', 'RINCON_TEST_2'] };
        fixture.state.media.players.RINCON_TEST_2 = { ...playback, context: { ...playback.context, deviceKey: 'RINCON_TEST_2' }, availableActions: ['leave'] };
        groups[0].members.push(member(2));
        groups.pop();
        await page.locator('.media-panel .media-heading button').click();
        await membership.getByRole('button', { name: 'Raum entfernen: Studio', exact: true }).waitFor();
        checks.push('adding a room preserves source and displayed destination across an ambiguous retry');

        await page.setViewportSize({ width: 360, height: 780 });
        await membership.getByRole('button', { name: 'Raum entfernen: Studio', exact: true }).click();
        await confirmation.getByText('Betroffener Raum: Studio').waitFor();
        await confirmation.getByRole('button', { name: 'Bestätigen' }).click();
        await page.locator('.media-operation').getByText('Dauerhaft angenommen').waitFor();
        const leaveRequest = fixture.state.media.requests.at(-1);
        assert.equal(leaveRequest.path, '/api/media/players/RINCON_TEST_2/leave');
        assert.equal(JSON.parse(leaveRequest.body).context.deviceKey, 'RINCON_TEST_2');
        fixture.state.media.operations.get(leaveRequest.key).status = 'desired_state_observed';
        await page.locator('.media-panel').getByRole('button', { name: 'Vorgang prüfen' }).click();
        playback.context = { ...playback.context, memberDeviceKeys: ['RINCON_TEST_0', 'RINCON_TEST_1'] };
        fixture.state.media.players.RINCON_TEST_2 = standaloneStudio;
        groups[0].members.pop();
        groups.push({ groupId: 'group-b', coordinatorDeviceId: 'RINCON_TEST_2', members: [member(2)] });
        await page.locator('.media-panel .media-heading button').click();
        await membership.getByRole('button', { name: 'Raum entfernen: Studio', exact: true }).waitFor({ state: 'detached' });
        await page.getByRole('button', { name: 'Aktualisieren', exact: true }).first().click();
        await membership.getByLabel('Raum hinzufügen', { exact: true }).selectOption('RINCON_TEST_2');
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true);
        await page.screenshot({ path: path.join(output, 'group-management-360-de.png'), fullPage: true });
        checks.push('removing a member addresses that room from the coordinator view at 360px');

        fixture.state.media.players.RINCON_TEST_2.availableActions = [];
        const beforeUnsupported = fixture.state.media.requests.length;
        await membership.getByRole('button', { name: 'Raum hinzufügen', exact: true }).click();
        await page.locator('.media-error').getByText('media.action_unavailable', { exact: true }).waitFor();
        assert.equal(fixture.state.media.requests.length, beforeUnsupported);
        assert.equal(await confirmation.count(), 0);
        checks.push('fresh source capabilities refuse unsupported grouping before confirmation or write');

        fixture.state.media.players.RINCON_TEST_2.availableActions = ['resume'];
        const beforeTargetChange = fixture.state.media.requests.length;
        await targetPicker.selectOption('RINCON_TEST_2');
        await page.locator('.media-target').getByText('Studio', { exact: false }).waitFor();
        assert.equal(fixture.state.media.requests.length, beforeTargetChange);
        await page.locator('.media-panel').getByRole('button', { name: 'Fortsetzen', exact: true }).click();
        await page.locator('.media-operation').getByText('Dauerhaft angenommen').waitFor();
        const playbackRequest = fixture.state.media.requests.at(-1);
        assert.equal(playbackRequest.path, '/api/media/players/RINCON_TEST_2/resume');
        assert.equal(JSON.parse(playbackRequest.body).context.coordinatorDeviceKey, 'RINCON_TEST_2');
        checks.push('changing playback destination sends nothing until an explicit playback action on the selected room');

        fixture.state.media.blocks.push({ operationId: '00000000-0000-0000-0000-0000000000aa', deviceKeys: ['RINCON_TEST_0'], action: 'queue-play', value: null, status: 'dispatch_started' });
        await page.goto(base + '/media');
        await page.getByText('Der Versand hat bereits begonnen.', { exact: false }).waitFor();
        assert.equal(await page.getByRole('button', { name: 'Benannte Vorgänge entsperren' }).count(), 0);
        checks.push('dispatch-started block is visible but cannot be risk-released');

        const noOverflow = async (width, culture = 'de') => {
            await page.setViewportSize({ width, height: 900 });
            await page.screenshot({ path: path.join(output, `media-${width}-${culture}.png`), fullPage: true });
            return page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);
        };
        assert.equal(await noOverflow(360), true);
        assert.equal(await noOverflow(1440), true);
        await page.getByRole('button', { name: 'English', exact: true }).click();
        await page.getByRole('heading', { level: 1, name: 'Media control', exact: true }).waitFor();
        assert.equal(await noOverflow(360, 'en'), true);
        checks.push('360/1440 layouts and English culture without page overflow');

        fixture.state.media.blocks = [];
        const mobileContext = await browser.newContext({ viewport: { width: 360, height: 780 }, deviceScaleFactor: 1,
            isMobile: true, hasTouch: true, locale: 'de-DE' });
        const mobilePage = await mobileContext.newPage();
        await mobilePage.goto(base + '/devices/RINCON_TEST_0');
        const mobilePanel = mobilePage.locator('.media-panel');
        await mobilePanel.getByText('Fixture song').waitFor();
        await mobilePanel.getByRole('button', { name: 'Lautstärke übernehmen' }).first().tap();
        await mobilePanel.locator('.media-operation').getByText('Dauerhaft angenommen').waitFor();
        assert.equal(fixture.state.media.requests.at(-1).path, '/api/media/players/RINCON_TEST_0/volume');
        assert.equal(await mobilePage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true);
        await mobilePage.screenshot({ path: path.join(output, 'media-touch-360-de.png'), fullPage: true });
        await mobileContext.close();
        checks.push('360px touch input submits an accepted player-volume operation without overflow');
        assert.deepEqual(failures, []);
        fs.writeFileSync(path.join(output, 'media-report.json'), JSON.stringify({ passedAtUtc: new Date().toISOString(), checks }, null, 2));
        console.log(checks.join('\n'));
    } catch (error) {
        if (page) {
            try {
                await page.screenshot({ path: path.join(output, 'media-failure.png'), fullPage: true });
                fs.writeFileSync(path.join(output, 'media-failure.html'), await page.content());
                console.error(`Media browser failure at ${page.url()}; screenshot and DOM saved in ${output}`);
            } catch (diagnosticError) { console.error(`Media browser diagnostics failed: ${diagnosticError}`); }
        }
        throw error;
    } finally {
        if (browser) await browser.close();
        web.kill();
        await new Promise(resolve => { if (web.exitCode !== null) resolve(); else web.once('exit', resolve); });
        await new Promise(resolve => fixture.server.close(resolve));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
