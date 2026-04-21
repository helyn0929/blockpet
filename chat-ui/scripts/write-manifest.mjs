/**
 * Regenerates manifest.txt from Assets/StreamingAssets/chat-ui (Android jar: copy).
 * Run after manually changing files under StreamingAssets/chat-ui.
 */
import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const target = path.resolve(__dirname, '../../Assets/StreamingAssets/chat-ui')

function walkFiles(dir, base = '') {
  const lines = []
  if (!fs.existsSync(dir)) {
    console.error('Missing folder:', dir)
    process.exit(1)
  }
  for (const name of fs.readdirSync(dir, { withFileTypes: true })) {
    if (name.name === 'manifest.txt') continue
    const rel = path.posix.join(base.replace(/\\/g, '/'), name.name).replace(/^\//, '')
    const full = path.join(dir, name.name)
    if (name.isDirectory()) lines.push(...walkFiles(full, rel))
    else lines.push(rel.replace(/\\/g, '/'))
  }
  return lines
}

const rels = walkFiles(target)
fs.writeFileSync(path.join(target, 'manifest.txt'), rels.join('\n') + '\n', 'utf8')
console.log('Wrote manifest.txt with', rels.length, 'entries')
