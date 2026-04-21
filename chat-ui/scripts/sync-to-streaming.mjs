import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const root = path.join(__dirname, '..')
const dist = path.join(root, 'dist')
const target = path.resolve(root, '../Assets/StreamingAssets/chat-ui')

function rmrf(dir) {
  if (fs.existsSync(dir)) fs.rmSync(dir, { recursive: true, force: true })
}

function copyRecursive(src, dst) {
  if (!fs.existsSync(src)) {
    console.error('Missing dist/. Run npm run build first.')
    process.exit(1)
  }
  fs.mkdirSync(dst, { recursive: true })
  for (const name of fs.readdirSync(src, { withFileTypes: true })) {
    const s = path.join(src, name.name)
    const d = path.join(dst, name.name)
    if (name.isDirectory()) copyRecursive(s, d)
    else fs.copyFileSync(s, d)
  }
}

function walkFiles(dir, base = '') {
  const lines = []
  for (const name of fs.readdirSync(dir, { withFileTypes: true })) {
    const rel = path.posix.join(base.replace(/\\/g, '/'), name.name).replace(/^\//, '')
    const full = path.join(dir, name.name)
    if (name.isDirectory()) lines.push(...walkFiles(full, rel))
    else lines.push(rel.replace(/\\/g, '/'))
  }
  return lines
}

rmrf(target)
copyRecursive(dist, target)
const rels = walkFiles(target)
fs.writeFileSync(path.join(target, 'manifest.txt'), rels.join('\n') + '\n', 'utf8')
console.log('Synced', rels.length, 'files to Assets/StreamingAssets/chat-ui/')
