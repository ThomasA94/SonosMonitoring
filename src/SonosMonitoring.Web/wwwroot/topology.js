const maps = new WeakMap();

// Only reported relationships become edges. Interface counters do not imply network paths.
function elements(groups, devices, labels) {
    const result = [];
    const known = new Map(devices.map(device => [device.deviceId, device]));
    groups.forEach((group, index) => {
        const prefix = `group-${index}-`;
        const parent = prefix + 'container';
        result.push({ data: { id: parent, label: `${labels.group} · ${group.groupId}`, kind: 'group' } });
        const members = new Map(group.members.map(member => [member.deviceId, member]));
        if (!members.has(group.coordinatorDeviceId)) members.set(group.coordinatorDeviceId, { deviceId: group.coordinatorDeviceId });
        for (const member of [...members.values()]) {
            if (member.parentDeviceId && !members.has(member.parentDeviceId)) members.set(member.parentDeviceId, { deviceId: member.parentDeviceId });
        }
        for (const [id, member] of members) {
            const device = known.get(id);
            const coordinator = id === group.coordinatorDeviceId;
            const name = member.roomName || device?.roomName || id;
            const quality = !device || !device.description.lastAttemptUtc ? 'reference' : !device.description.lastAttemptSucceeded ? 'failed' : device.description.isStale ? 'stale' : 'observed';
            result.push({ data: { id: prefix + id, parent, deviceId: id, kind: coordinator ? 'coordinator' : 'member', quality,
                label: `${name}\n${coordinator ? labels.coordinator : (device?.modelName || labels.members)}${labels.summaries?.[id]?.length ? '\n' + labels.summaries[id].join('\n') : ''}` } });
            const source = member.parentDeviceId || (coordinator ? null : group.coordinatorDeviceId);
            if (source && source !== id) result.push({ data: { id: prefix + 'edge-' + id, source: prefix + source, target: prefix + id,
                kind: member.parentDeviceId ? 'satellite' : 'membership' } });
        }
    });
    return result;
}

export function update(element, groups, devices, labels, callback) {
    if (!element?.isConnected) return;
    let state = maps.get(element);
    if (!state) {
        const cy = window.cytoscape({ container: element, minZoom: 0.15, maxZoom: 3, wheelSensitivity: 0.2,
            style: [
                { selector: 'node', style: { 'background-color': '#248c86', 'label': 'data(label)', 'color': '#263449', 'font-size': 12, 'text-wrap': 'wrap', 'text-max-width': 260, 'text-valign': 'bottom', 'text-margin-y': 9, 'width': 44, 'height': 44, 'border-width': 3, 'border-color': '#fff' } },
                { selector: 'node[kind="coordinator"]', style: { 'background-color': '#7460b5', 'width': 58, 'height': 58, 'shape': 'round-rectangle' } },
                { selector: 'node[quality="reference"]', style: { 'background-color': '#94a3b8', 'border-style': 'dashed' } },
                { selector: 'node[quality="failed"]', style: { 'border-color': '#c94949', 'border-width': 6 } },
                { selector: 'node[quality="stale"]', style: { 'border-color': '#ce922a', 'border-width': 6 } },
                { selector: 'node[kind="group"]', style: { 'shape': 'round-rectangle', 'background-color': '#eae8f5', 'background-opacity': 0.45, 'border-width': 1, 'border-color': '#d4cce8', 'padding': 32, 'text-valign': 'top', 'text-margin-y': -10, 'font-size': 13 } },
                { selector: 'edge', style: { 'width': 2, 'line-color': '#a2aebb', 'curve-style': 'unbundled-bezier', 'control-point-distances': 100, 'control-point-weights': 0.5, 'line-style': 'dashed', 'target-arrow-shape': 'triangle', 'target-arrow-color': '#a2aebb' } },
                { selector: 'edge[kind="satellite"]', style: { 'width': 3, 'line-color': '#7460b5', 'line-style': 'solid', 'target-arrow-color': '#7460b5' } },
                { selector: 'node:selected', style: { 'overlay-color': '#7460b5', 'overlay-opacity': 0.18, 'overlay-padding': 8 } }
            ]
        });
        state = { cy, structure: '', callback };
        cy.on('tap', 'node[deviceId]', event => state.callback.invokeMethodAsync('SelectDevice', event.target.data('deviceId')).catch(() => {}));
        state.key = event => {
            const pan = { ArrowLeft: { x: 40, y: 0 }, ArrowRight: { x: -40, y: 0 }, ArrowUp: { x: 0, y: 40 }, ArrowDown: { x: 0, y: -40 } }[event.key];
            if (pan) cy.panBy(pan);
            else if (event.key === '+' || event.key === '=') command(element, 'in');
            else if (event.key === '-') command(element, 'out');
            else if (event.key === '0') command(element, 'fit');
            else return;
            event.preventDefault();
        };
        element.addEventListener('keydown', state.key);
        state.resize = new ResizeObserver(() => cy.resize());
        state.resize.observe(element);
        state.removal = new MutationObserver(() => { if (!element.isConnected) destroy(element); });
        state.removal.observe(document.body, { childList: true, subtree: true });
        maps.set(element, state);
    }
    state.callback = callback;
    const next = elements(groups, devices, labels);
    const structure = JSON.stringify(next.map(item => [item.data.id, item.data.source, item.data.target, item.data.parent]));
    const positions = new Map(state.cy.nodes().map(node => [node.id(), { ...node.position() }]));
    const zoom = state.cy.zoom();
    const pan = { ...state.cy.pan() };
    next.forEach(item => { if (positions.has(item.data.id)) item.position = positions.get(item.data.id); });
    state.cy.json({ elements: next });
    if (state.structure !== structure) arrange(state.cy);
    else { state.cy.zoom(zoom); state.cy.pan(pan); }
    state.structure = structure;
}

function arrange(cy) {
    // Compact columns keep small households readable on phones; larger maps remain zoomable.
    const groups = cy.nodes('[kind="group"]');
    const columns = Math.max(1, Math.min(groups.length, Math.floor(cy.width() / 340)));
    const memberColumns = cy.width() < 900 ? 1 : 2;
    let rowY = 70;
    for (let start = 0; start < groups.length; start += columns) {
        let rowHeight = 0;
        cy.batch(() => {
            for (let offset = 0; offset < columns && start + offset < groups.length; offset++) {
                const members = groups[start + offset].children();
                const root = members.filter('[kind="coordinator"]');
                const others = members.filter('[kind!="coordinator"]');
                const centerX = offset * 680 + 340;
                root.position({ x: centerX, y: rowY });
                others.forEach((node, index) => node.position({
                    x: centerX + (memberColumns === 1 ? 0 : (index % 2 === 0 ? -165 : 165)),
                    y: rowY + 260 + Math.floor(index / memberColumns) * 260
                }));
                rowHeight = Math.max(rowHeight, 350 + Math.ceil(others.length / memberColumns) * 260);
            }
        });
        rowY += rowHeight;
    }
    cy.fit(undefined, 25);
}

export function command(element, action) {
    const cy = maps.get(element)?.cy;
    if (!cy) return;
    if (action === 'layout') arrange(cy);
    else if (action === 'fit') cy.fit(undefined, 45);
    else cy.zoom({ level: Math.max(cy.minZoom(), Math.min(cy.maxZoom(), cy.zoom() * (action === 'in' ? 1.25 : 0.8))),
        renderedPosition: { x: cy.width() / 2, y: cy.height() / 2 } });
}

export function destroy(element) {
    const state = maps.get(element);
    if (!state) return;
    state.resize.disconnect();
    state.removal.disconnect();
    element.removeEventListener('keydown', state.key);
    state.cy.destroy();
    maps.delete(element);
}
