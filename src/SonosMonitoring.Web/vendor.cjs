const fs = require('node:fs');
const path = require('node:path');
const root = __dirname;
const target = path.join(root, 'wwwroot/vendor/cytoscape');
const check = process.argv.includes('--check');
if (!check) fs.mkdirSync(target, { recursive: true });
for (const [source, output] of [['dist/cytoscape.min.js', 'cytoscape.min.js'], ['LICENSE', 'LICENSE']]) {
    const input = path.join(root, 'node_modules/cytoscape', source);
    const destination = path.join(target, output);
    if (check) {
        if (!fs.readFileSync(input).equals(fs.readFileSync(destination))) throw new Error(`Vendored asset differs from the locked package: ${output}`);
    } else fs.copyFileSync(input, destination);
}
