const http = require('node:http');

// A deterministic API boundary; these observations never contact physical players.
exports.createFixture = async () => {
    const now = new Date().toISOString();
    const source = name => ({ name, lastAttemptSucceeded: true, lastAttemptUtc: now, lastSuccessUtc: now, isStale: false, errorCode: null });
    const devices = ['Living room', 'Kitchen', 'Studio'].map((roomName, index) => ({
        deviceId: `RINCON_TEST_${index}`, address: `192.0.2.${index + 1}`, roomName, modelName: ['Sonos One', 'Sonos Era 100', 'Sonos Five'][index],
        firstSeenUtc: now, lastSeenUtc: now, description: { ...source('description'), isStale: index === 2 },
        interfaceStatus: source('ifconfig'), topologyStatus: source('topology'), metricLabel: `device-${index}`,
        gena: { enabled: true, subscribed: true, lastAttemptUtc: now, expiresUtc: now, lastNotificationUtc: now, initialNotificationReceived: true },
        interfaces: [{ interface: 'wlan0', receiveBytes: 1234567, transmitBytes: 7654321, receivePackets: 1200, transmitPackets: 800, receiveErrors: 0, transmitErrors: 0, receiveDropped: 2, transmitDropped: 0 }],
        groups: [{ groupId: 'GROUP_TEST', coordinatorDeviceId: 'RINCON_TEST_0', members: [0, 1, 2].map(i => ({ deviceId: `RINCON_TEST_${i}`, roomName: ['Living room', 'Kitchen', 'Studio'][i], parentDeviceId: null })) }]
    }));
    const snapshot = { enabled: true, heartbeatUtc: now, lastSweepUtc: now, devices, ranges: [{ cidr: '192.0.2.0/24', lastAttemptUtc: now, lastCompletedUtc: now, lastAttemptSucceeded: true, addressesProbed: 254, devicesFound: 3 }], capacityExceeded: false,
        ssdp: { enabled: false, candidateCount: 0, failedInterfaces: 0, limitReached: false }, topologyDiscovery: { lastSelectionUtc: now, candidateCount: 2, limitReached: false } };
    snapshot.sweepDurationSeconds = 0.45;
    snapshot.probeAttemptsTotal = 20;
    snapshot.probeFailuresTotal = 2;
    snapshot.probeTimeoutsTotal = 1;
    snapshot.discoveryAddressesProbed = 254;
    snapshot.discoveryDevicesFound = 3;
    for (const device of devices) {
        device.groups[0].members[2].parentDeviceId = 'RINCON_TEST_0';
        device.groups[0].members.push({ deviceId: 'RINCON_UNOBSERVED', roomName: 'Reported player', parentDeviceId: null });
    }
    devices[0].monitoringEnabled = true;
    devices[0].effectiveIntervalSeconds = 15;
    devices[0].softwareVersion = 'TEST-FIRMWARE';
    devices[0].hardwareVersion = 'TEST-HARDWARE';
    devices[0].modelNumber = 'TEST-MODEL';
    devices[0].probeDurationSeconds = 0.12;
    devices[0].wirelessStatus = source('wireless');
    devices[0].wireless = { wifiMode: 'STATION_MODE', connectionType: 'WiFi', sonosNetDisabled: false };
    devices[0].ethernetStatus = source('ethernet');
    devices[0].ethernetPorts = [{ port: '00', linkUp: false, reportedSpeed: 0 }];
    devices[0].playbackStatus = source('playback');
    devices[0].playback = { transportState: 'PLAYING', transportStatus: 'OK' };
    devices[0].volumeStatus = source('volume');
    devices[0].volume = 24;
    devices[0].muted = false;
    devices[0].interfaceRates = [
        { interface: 'wlan0', receiveBytesPerSecond: 99999, transmitBytesPerSecond: 99999, sampledAtUtc: now, intervalSeconds: 15 },
        { interface: 'br0', receiveBytesPerSecond: 1000, transmitBytesPerSecond: 2000, sampledAtUtc: now, intervalSeconds: 15 }
    ];
    devices[1].interfaceStatus = { ...source('ifconfig'), lastAttemptSucceeded: false, lastSuccessUtc: null, errorCode: 'monitoring_disabled' };
    devices[1].monitoringEnabled = false;
    devices[1].interfaces = [];
    devices[1].gena.enabled = false;
    devices[2].interfaceStatus = { ...source('ifconfig'), lastAttemptSucceeded: false, errorCode: 'not_supported' };
    const catalog = [];
    const metric = (selector, unit, scope = 'interface') => catalog.push({ selector, unit, scope, labelKey: scope === 'interface' ? 'interface' : scope === 'source' ? 'source' : 'device_id' });
    for (const direction of ['receive', 'transmit']) {
        for (const [counter, unit] of [['bytes', 'bytes'], ['packets', 'packets'], ['errors', 'errors'], ['dropped', 'drops']]) {
            metric(`${direction}_${counter}`, unit);
            metric(counter === 'bytes' ? `${direction}_rate` : `${direction}_${counter}_rate`, `${unit}/second`);
        }
    }
    metric('probe_success', 'boolean', 'device');
    metric('source_success', 'boolean', 'source'); metric('source_fresh', 'boolean', 'source'); metric('source_age', 'seconds', 'source');
    metric('gena_subscribed', 'boolean', 'device'); metric('gena_initial_received', 'boolean', 'device');
    metric('gena_last_notification_age', 'seconds', 'device'); metric('probe_duration', 'seconds', 'device');
    metric('volume', 'percent', 'device'); metric('muted', 'boolean', 'device');
    const document = { format: 'sonosmonitoring.collection-snapshot', schemaVersion: 1, capturedAtUtc: now, privacy: 'reduced-identifiers-v1', snapshot };
    const state = { collectionFailure: false, alertsFailure: false, pending: 0, policy: null, ambiguousOnce: true, conflictOnce: false, requests: [], metricRequests: [], metricEmpty: false, receipts: new Map(), logsFailure: false, logRequests: [] };
    const logs = [
        { timestampUtc: '2026-10-01T10:20:30Z', source: 'api', level: 'Warning', category: 'SonosMonitoring.Collector', eventId: 0, message: 'Sonos discovery: no players found. Check Docker/LAN subnet overlap. <script>window.logInjected = true</script>' },
        { timestampUtc: '2026-10-01T10:15:00Z', source: 'projections', level: 'Error', category: 'EventProjection', eventId: 7, message: 'Connection to broker unavailable.' },
        { timestampUtc: '2026-10-01T09:00:00Z', source: 'web', level: 'Information', category: 'SonosMonitoring.Web', eventId: 0, message: 'Application started.' }
    ];
    const mediaContext = { deviceKey: devices[0].deviceId, householdId: 'HOUSEHOLD_TEST', coordinatorDeviceKey: devices[0].deviceId,
        memberDeviceKeys: [devices[0].deviceId, devices[1].deviceId], sourceToken: 'source-1', queueRevision: 'queue-7' };
    state.media = {
        enabled: false, generation: '00000000-0000-0000-0000-000000000001', blocks: [], operations: new Map(), requests: [], riskRequests: [], ambiguousOnce: true, stateReads: 0,
        actions: ['resume', 'pause', 'stop', 'volume', 'mute', 'unmute', 'group-volume', 'group-mute', 'group-unmute', 'seek', 'mode',
            'crossfade', 'start-favorite', 'start-stream', 'queue-play', 'queue-append', 'queue-remove', 'queue-reorder', 'queue-clear', 'join', 'leave'],
        playback: { context: mediaContext, observedAtUtc: now, source: 'queue', transportState: 'PLAYING', volume: 24, muted: false,
            groupVolume: 30, groupMuted: false, title: 'Fixture song', artist: 'Fixture artist', album: 'Fixture album',
            durationSeconds: 180, positionSeconds: 42, playMode: 'normal', crossfade: false }
    };
    state.configuration = { revision: '00000000-0000-0000-0000-000000000000', settings: {
        enabled: false, allowedCidrs: [], seedAddresses: [], ssdpEnabled: false, ssdpInterfaceAddresses: [], ssdpSearchWindow: '00:00:03', ssdpMaxDatagrams: 64,
        pollInterval: '00:00:15', discoveryInterval: '00:10:00', staleAfter: '00:00:45', maxScanAddresses: 1024, maxKnownDevices: 256, maxTopologyCandidates: 32,
        maxParallelism: 16, requestTimeout: '00:00:02', sweepTimeout: '00:01:30', maxResponseBytes: 262144,
        gena: { enabled: false, listenAddress: '0.0.0.0', callbackAddress: null, port: 48081, requestedLeaseSeconds: 1800, initialNotificationTimeout: '00:00:15', minimumPollInterval: '00:00:02' }
    } };
    state.settingsRequests = [];
    const send = (response, status, data) => { response.writeHead(status, { 'content-type': 'application/json' }); response.end(JSON.stringify(data)); };
    const server = http.createServer(async (request, response) => {
        const url = new URL(request.url, 'http://localhost');
        if (url.pathname === '/api/diagnostics/logs') {
            state.logRequests.push(url.search);
            if (state.logsFailure) return send(response, 503, { title: 'request.failed' });
            const levels = ['Information', 'Warning', 'Error', 'Critical'];
            const entries = logs.filter(entry => (!url.searchParams.get('source') || entry.source === url.searchParams.get('source')) &&
                levels.indexOf(entry.level) >= levels.indexOf(url.searchParams.get('minimumLevel') || 'Information') &&
                entry.message.toLowerCase().includes((url.searchParams.get('search') || '').toLowerCase()));
            return send(response, 200, { entries, sources: [{ source: 'api', state: 'available', skippedLines: 0 }, { source: 'projections', state: 'available', skippedLines: 1 }, { source: 'web', state: 'missing', skippedLines: 0 }], truncated: false, limit: 200 });
        }
        if (url.pathname === '/api/collection/configuration') {
            if (request.method === 'GET') return send(response, 200, state.configuration);
            let body = '';
            for await (const chunk of request) body += chunk;
            const key = request.headers['idempotency-key'];
            state.settingsRequests.push({ key, body });
            const submitted = JSON.parse(body);
            if (state.settingsInvalid) { state.settingsInvalid = false; return send(response, 400, { title: 'collection.configuration.invalid' }); }
            if (state.settingsConflict) { state.settingsConflict = false; return send(response, 409, { title: 'request.conflict' }); }
            if (key !== state.configuration.revision && submitted.revision !== state.configuration.revision) return send(response, 409, { title: 'request.conflict' });
            state.configuration = { ...submitted, revision: key };
            if (state.settingsAmbiguous) { state.settingsAmbiguous = false; return send(response, 503, { title: 'temporarily.unavailable' }); }
            return send(response, 200, state.configuration);
        }
        if (url.pathname === '/api/media/control' && request.method === 'GET') {
            return send(response, 200, { enabled: state.media.enabled, generation: state.media.generation, blocks: state.media.blocks });
        }
        if (url.pathname === '/api/media/control/enable' || url.pathname === '/api/media/control/disable') {
            state.media.enabled = url.pathname.endsWith('/enable');
            state.media.generation = request.headers['idempotency-key'];
            return send(response, 200, { enabled: state.media.enabled, generation: state.media.generation, blocks: state.media.blocks });
        }
        if (url.pathname === '/api/media/control/accept-risk') {
            let body = '';
            for await (const chunk of request) body += chunk;
            state.media.riskRequests.push({ key: request.headers['idempotency-key'], body });
            const acknowledgement = JSON.parse(body);
            if (acknowledgement.confirmDelayedEffect !== true) return send(response, 400, { code: 'media.risk.confirmation.required' });
            const ids = acknowledgement.operationIds;
            if (ids.length === 0 || ids.some(id => state.media.blocks.some(block => block.operationId === id && block.status === 'outcome_unknown') === false)) {
                return send(response, 409, { code: 'media.risk.targets.changed' });
            }
            state.media.blocks = [];
            return send(response, 200, { enabled: state.media.enabled, generation: state.media.generation, blocks: [] });
        }
        if (/^\/api\/media\/players\/[^/]+\/state$/.test(url.pathname) && request.method === 'GET') {
            state.media.stateReads++;
            const player = decodeURIComponent(url.pathname.split('/')[4]);
            if (state.media.players?.[player]) return send(response, 200, state.media.players[player]);
            return send(response, 200, { ...state.media.playback, availableActions: state.media.actions });
        }
        if (/^\/api\/media\/players\/[^/]+\/favorites$/.test(url.pathname) && request.method === 'GET') {
            return send(response, 200, { items: [{ reference: 'favorite:opaque-1', title: 'Fixture favorite', artist: 'Fixture artist', album: null, index: null }],
                offset: Number(url.searchParams.get('offset')), total: 1, revision: 'favorites-2' });
        }
        if (/^\/api\/media\/players\/[^/]+\/queue$/.test(url.pathname) && request.method === 'GET') {
            return send(response, 200, { items: [{ reference: 'queue:opaque-1', title: 'Fixture song', artist: 'Fixture artist', album: 'Fixture album', index: 1 }],
                offset: Number(url.searchParams.get('offset')), total: 1, revision: 'queue-7' });
        }
        if (/^\/api\/media\/players\/[^/]+\/[^/]+$/.test(url.pathname) && request.method === 'POST') {
            let body = '';
            for await (const chunk of request) body += chunk;
            const key = request.headers['idempotency-key'];
            state.media.requests.push({ key, path: url.pathname, body });
            if (state.media.operations.has(key)) return send(response, 202, state.media.operations.get(key));
            const action = url.pathname.split('/').at(-1);
            const operation = { operationId: key, action, deviceKey: decodeURIComponent(url.pathname.split('/')[4]), status: 'accepted', acceptedAtUtc: now,
                code: null, step: -1, commitToken: 'media-token-1', eventSequence: 1 };
            state.media.operations.set(key, operation);
            if (state.media.ambiguousOnce) { state.media.ambiguousOnce = false; return send(response, 503, { code: 'temporarily.unavailable' }); }
            return send(response, 202, operation);
        }
        if (/^\/api\/media\/operations\/[^/]+$/.test(url.pathname) && request.method === 'GET') {
            const operation = state.media.operations.get(url.pathname.split('/').at(-1));
            return operation ? send(response, 200, operation) : send(response, 404, { code: 'media.operation.not_found' });
        }
        if (url.pathname === '/api/collection') return send(response, state.collectionFailure ? 503 : 200, state.collectionFailure ? { title: 'collection.unavailable' } : snapshot);
        if (url.pathname === '/api/collection/export') return send(response, 200, document);
        if (url.pathname === '/api/alerts') return send(response, state.alertsFailure ? 502 : 200, state.alertsFailure ? { title: 'alerts.unavailable' } : { evaluatedAtUtc: now, evaluationDurationSeconds: .1, messages: ['firing', 'pending', 'recovered'].map((s, i) => ({ identity: `alert-${i}`, occurrenceIdentity: `occurrence-${i}`, ruleName: 'SonosDeviceDescriptionUnavailable', state: s, activeAtUtc: now, recoveryAtUtc: s === 'recovered' ? now : null, evaluatedAtUtc: now, evaluationDurationSeconds: .1, severity: 'warning', scope: 'device', deviceLabel: `device-${i}` })) });
        if (url.pathname === '/api/collection/history') {
            const after = Number(url.searchParams.get('after'));
            return send(response, 200, { changes: after === 0 ? [{ sequence: 5, kind: 'gap', recordedAtUtc: now, reason: 'collector_restart' }, { sequence: 6, kind: 'baseline', recordedAtUtc: now, after: { deviceId: devices[0].deviceId, address: devices[0].address, descriptionState: 'available', topologyState: 'available', groups: [] } }] : [], nextCursor: 6, earliestCursor: 5, lastRecordedAtUtc: now, retentionDays: 90, maxEntries: 20000 });
        }
        if (url.pathname === '/api/metrics/history/catalog') return send(response, 200, { metrics: catalog });
        if (url.pathname === '/api/metrics/history') {
            state.metricRequests.push(Object.fromEntries(url.searchParams));
            const start = Date.parse(url.searchParams.get('start')) / 1000;
            const step = Number(url.searchParams.get('stepSeconds'));
            const metric = url.searchParams.get('metric');
            return send(response, 200, { metric, deviceKey: url.searchParams.get('deviceKey'), unit: catalog.find(entry => entry.selector === metric)?.unit || 'unknown', stepSeconds: step, start: url.searchParams.get('start'), end: url.searchParams.get('end'), data: { resultType: 'matrix', result: state.metricEmpty ? [] : [{ metric: { interface: 'wlan0' }, values: Array.from({ length: 120 }, (_, i) => [start + i * step, i === 40 ? 'NaN' : String(1000 + (i % 12) * 210)]) }] } });
        }
        if (url.pathname.startsWith('/api/monitoring/')) {
            if (request.method === 'GET') {
                if (url.searchParams.has('minSequence') && state.pending-- > 0) return send(response, 202, { code: 'projection.pending' });
                return state.policy ? send(response, 200, state.policy) : send(response, 404, { code: 'monitoring.not_found' });
            }
            let body = '';
            for await (const chunk of request) body += chunk;
            const key = request.headers['idempotency-key'];
            state.requests.push({ key, path: url.pathname, body });
            if (state.conflictOnce) { state.conflictOnce = false; return send(response, 409, { code: 'ddd.concurrency.conflict' }); }
            if (state.receipts.has(key)) return send(response, 202, { ...state.receipts.get(key), alreadyCommitted: true });
            const sequence = (state.policy?.eventSequence || 0) + 1;
            const token = `opaque-token-${sequence}`;
            state.policy = { aggregateId: 'stream', deviceKey: devices[0].deviceId, enabled: url.pathname.endsWith('/enable'), intervalSeconds: JSON.parse(body).intervalSeconds || 15, eventSequence: sequence, updatedAtUtc: now, concurrencyToken: token };
            const receipt = { aggregateId: 'stream', concurrencyToken: token, eventSequence: sequence, alreadyCommitted: false, queryUrl: `/api/monitoring/${devices[0].deviceId}?minSequence=${sequence}` };
            state.receipts.set(key, receipt);
            state.pending = 2;
            if (state.ambiguousOnce) { state.ambiguousOnce = false; return send(response, 503, { title: 'temporarily.unavailable' }); }
            return send(response, 202, receipt);
        }
        send(response, 404, { title: 'not_found' });
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    return { server, state, document, url: `http://127.0.0.1:${server.address().port}/` };
};
