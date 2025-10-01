#!/usr/bin/env node
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const CONTENT_DIR = join(process.cwd(), 'content');
const ALLOWED_TAGS = new Set(['cli', 'build', 'generation', 'create', 'init', 'pull', 'sync']);
const REQUIRED_FIELDS = ['title', 'description'];

let errors = 0;

function walk(dir) {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
        const full = join(dir, entry.name);
        if (entry.isDirectory()) walk(full); else if (entry.name.endsWith('.md')) validateFile(full);
    }
}

function parseFrontmatter(raw) {
    if (!raw.startsWith('---')) return null;
    const end = raw.indexOf('\n---', 3);
    if (end === -1) return null;
    const header = raw.substring(3, end).trim();
    const body = raw.substring(end + 4);
    const obj = {};
    for (const line of header.split(/\r?\n/)) {
        if (!line.trim() || line.trim().startsWith('#')) continue;
        const m = line.match(/^([A-Za-z0-9_-]+):\s*(.*)$/);
        if (m) {
            let val = m[2].trim();
            if (val === 'true') val = true; else if (val === 'false') val = false; else if (/^\[.*\]$/.test(val)) {
                try { val = JSON.parse(val); } catch { /* ignore */ }
            }
            obj[m[1]] = val;
        }
    }
    return { data: obj, body };
}

function validateFile(file) {
    const raw = readFileSync(file, 'utf8');
    const fm = parseFrontmatter(raw);
    if (!fm) {
        console.warn(`[WARN] No frontmatter: ${file}`);
        return;
    }
    const { data } = fm;
    for (const f of REQUIRED_FIELDS) {
        if (!data[f]) {
            console.error(`[ERR] Missing required field '${f}' in ${file}`);
            errors++;
        }
    }
    if (data.aiTags) {
        if (!Array.isArray(data.aiTags)) {
            console.error(`[ERR] aiTags must be array in ${file}`); errors++;
        } else {
            for (const t of data.aiTags) {
                if (!ALLOWED_TAGS.has(t)) {
                    console.error(`[ERR] Unknown aiTag '${t}' in ${file}`);
                    errors++;
                }
            }
        }
    }
}

walk(CONTENT_DIR);

if (errors > 0) {
    console.error(`Validation failed with ${errors} error(s).`);
    process.exit(1);
} else {
    console.log('Frontmatter validation passed.');
}
