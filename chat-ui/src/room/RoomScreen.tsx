import { useMemo, useState } from 'react'
import type { RoomSummary } from '../chat/types'
import { requestBack, requestCreateRoom, requestJoinRoom, requestSetRoom } from '../chat/bridge'
import './room-screen.css'

function pctHearts(s: RoomSummary): number {
  const cur = s.currentHealth ?? 0
  const pct = Math.round((cur / 86400) * 100)
  if (!Number.isFinite(pct)) return 0
  return Math.max(0, Math.min(100, pct))
}

function createCode(): string {
  const alphabet = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789'
  let s = ''
  for (let i = 0; i < 6; i++) s += alphabet[Math.floor(Math.random() * alphabet.length)]
  return s
}

export function RoomScreen(props: { title?: string; rooms: RoomSummary[]; currentRoomId?: string }) {
  const [roomInput, setRoomInput] = useState('')

  const rooms = props.rooms ?? []
  const title = useMemo(() => (props.title && props.title.length > 0 ? props.title : '選擇房間'), [props.title])
  const currentRoomId = props.currentRoomId ?? ''

  return (
    <div className="bp-roomRoot">
      <div className="bp-room">
        <header className="bp-room__header">
          <div className="bp-room__title">{title}</div>
          <div className="bp-room__headerBtns">
            <button type="button" className="bp-room__back" onClick={() => requestBack()}>
              返回
            </button>
          </div>
        </header>

        {currentRoomId && rooms.length === 0 && (
          <div className="bp-room__continueBanner">
            <button type="button" className="bp-room__btn bp-room__btn--continue" onClick={() => requestSetRoom(currentRoomId)}>
              繼續目前房間（{currentRoomId}）
            </button>
          </div>
        )}

        <div className="bp-room__grid">
          {rooms.map((r) => (
            <button type="button" key={r.roomId} className="bp-room__tile" onClick={() => requestSetRoom(r.roomId)}>
              <div className="bp-room__tileTop">
                <div className="bp-room__tileName">{r.name || `Room ${r.roomId}`}</div>
                <div className="bp-room__tileCode">{r.roomId}</div>
              </div>
              <div className="bp-room__tileMeta">
                <span>Pet #{r.petIndex ?? 0}</span>
                <span className="bp-room__dot" />
                <span>Hearts {pctHearts(r)}%</span>
              </div>
            </button>
          ))}
        </div>

        <div className="bp-room__card">
          <div className="bp-room__actions">
            <button
              type="button"
              className="bp-room__btn"
              onClick={() => {
                const code = createCode()
                requestCreateRoom(code, `Room ${code}`)
              }}
            >
              建立新房間
            </button>
          </div>

          <div className="bp-room__divider" />

          <div className="bp-room__label">加入既有房間（輸入 code）</div>
          <div className="bp-room__joinRow">
            <input
              className="bp-room__input"
              placeholder="輸入房間碼"
              value={roomInput}
              onChange={(e) => setRoomInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') requestJoinRoom(roomInput)
              }}
            />
            <button type="button" className="bp-room__btn bp-room__btn--small" onClick={() => requestJoinRoom(roomInput)}>
              加入
            </button>
          </div>
          <div className="bp-room__hint">一張卡片＝一個房間；你可以加入/建立很多房間，分別養不同寵物。</div>
        </div>
      </div>
    </div>
  )
}

