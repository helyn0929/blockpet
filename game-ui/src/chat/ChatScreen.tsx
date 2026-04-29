import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ChatMessage, UnityChatPayload } from './types'
import { displayNameOf } from './types'
import {
  notifyReplySelect,
  requestBack,
  requestLeaveChat,
  requestClearReply,
  requestOpenAlbum,
  sendMessage,
} from './bridge'
import './chat-screen.css'

function previewSnippet(s: string): string {
  const flat = s.replace(/\r?\n/g, ' ').trim()
  if (flat.length <= 90) return flat
  return flat.slice(0, 90).trimEnd() + '…'
}

function isSelfMessage(m: ChatMessage, local: string): boolean {
  return (m.userName ?? '') === local || (m.displayName ?? '') === local
}

export function ChatScreen(props: { init: Extract<UnityChatPayload, { kind: 'init' }> }) {
  const init = props.init
  const [messages, setMessages] = useState<ChatMessage[]>(init.messages ?? [])
  const [memberCount, setMemberCount] = useState(0)
  const [roomId, setRoomId] = useState(init.roomId ?? '')
  const [localDisplayName, setLocalDisplayName] = useState('')
  const [mineOnRight, setMineOnRight] = useState(true)
  const [animalB64, setAnimalB64] = useState<string | null>(null)
  const [replyTarget, setReplyTarget] = useState<ChatMessage | null>(null)
  const [draft, setDraft] = useState('')
  const [isComposing, setIsComposing] = useState(false)
  const [useNativeComposer, setUseNativeComposer] = useState(false)
  const [showRoomInfo, setShowRoomInfo] = useState(false)
  const [codeCopied, setCodeCopied] = useState(false)
  const bottomRef = useRef<HTMLDivElement>(null)

  function handleCopyRoomCode() {
    navigator.clipboard?.writeText(roomId).catch(() => {})
    setCodeCopied(true)
    setTimeout(() => setCodeCopied(false), 2000)
  }

  const applyPayload = useCallback((detail: unknown) => {
    const p = detail as UnityChatPayload
    if (!p || typeof p !== 'object' || !('kind' in p)) return
    switch (p.kind) {
      case 'init':
        setMessages(p.messages ?? [])
        setMemberCount(p.memberCount ?? 0)
        if (p.roomId) setRoomId(p.roomId)
        setLocalDisplayName(p.localDisplayName ?? '')
        setMineOnRight(!!p.mineMessagesOnRight)
        setAnimalB64(p.animalImageBase64 && p.animalImageBase64.length > 0 ? p.animalImageBase64 : null)
        setUseNativeComposer(!!p.useNativeComposer)
        setReplyTarget(null)
        break
      case 'append': {
        const m = p.message
        if (!m) return
        setMessages((prev) => {
          if (
            prev.some(
              (x) =>
                x.messageId &&
                m.messageId &&
                x.messageId === m.messageId &&
                x.timestamp === m.timestamp,
            )
          )
            return prev
          if (
            prev.some(
              (x) =>
                x.userName === m.userName &&
                x.message === m.message &&
                x.timestamp === m.timestamp,
            )
          )
            return prev
          return [...prev, m]
        })
        break
      }
      case 'header':
        setMemberCount(p.memberCount ?? 0)
        break
      case 'clearReply':
        setReplyTarget(null)
        break
      case 'clearMessages':
        setMessages([])
        setReplyTarget(null)
        break
      default:
        break
    }
  }, [])

  useEffect(() => {
    const onUnity = (e: Event) => {
      const ce = e as CustomEvent<unknown>
      applyPayload(ce.detail)
    }
    window.addEventListener('blockpet-chat', onUnity)
    return () => window.removeEventListener('blockpet-chat', onUnity)
  }, [applyPayload])

  // When App switches rooms and passes a new init, apply it immediately.
  useEffect(() => {
    setMessages(init.messages ?? [])
    setMemberCount(init.memberCount ?? 0)
    if (init.roomId) setRoomId(init.roomId)
    setLocalDisplayName(init.localDisplayName ?? '')
    setMineOnRight(!!init.mineMessagesOnRight)
    setAnimalB64(init.animalImageBase64 && init.animalImageBase64.length > 0 ? init.animalImageBase64 : null)
    setUseNativeComposer(!!init.useNativeComposer)
    setReplyTarget(null)
    setDraft('')
  }, [init])

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' })
  }, [messages])

  const title = useMemo(() => {
    const n = Math.max(0, memberCount)
    return `Chatroom (${n})`
  }, [memberCount])

  const onSend = () => {
    sendMessage(draft, replyTarget, previewSnippet)
    setDraft('')
    setReplyTarget(null)
  }

  return (
    <div className="bp-chat">
      {showRoomInfo && (
        <div className="bp-chat__roomInfoOverlay" onClick={() => setShowRoomInfo(false)}>
          <div className="bp-chat__roomInfoCard" onClick={(e) => e.stopPropagation()}>
            <div className="bp-chat__roomInfoTitle">房間資訊</div>
            <div className="bp-chat__roomInfoLabel">房間碼</div>
            <div className="bp-chat__roomInfoCode">{roomId || '—'}</div>
            <div className="bp-chat__roomInfoHint">將此碼分享給朋友，他們在「房間選擇」輸入後即可加入</div>
            <button
              type="button"
              className="bp-chat__roomInfoCopyBtn"
              onClick={handleCopyRoomCode}
            >
              {codeCopied ? '已複製！' : '複製房間碼'}
            </button>
            <button
              type="button"
              className="bp-chat__roomInfoCloseBtn"
              onClick={() => setShowRoomInfo(false)}
            >
              關閉
            </button>
          </div>
        </div>
      )}

      {!useNativeComposer ? (
        <header className="bp-chat__header">
          <button type="button" className="bp-chat__animalBtn" aria-label="Back" onClick={() => requestBack()}>
            {animalB64 ? (
              <img className="bp-chat__animalImg" src={`data:image/png;base64,${animalB64}`} alt="" />
            ) : (
              <span className="bp-chat__animalFallback" aria-hidden />
            )}
          </button>
          <button type="button" className="bp-chat__headerTitle" onClick={() => setShowRoomInfo(true)}>
            {title} <span className="bp-chat__headerInfoIcon">ⓘ</span>
          </button>
          <button
            type="button"
            className="bp-chat__leaveBtn"
            aria-label="Leave chat room"
            onClick={() => requestLeaveChat()}
          >
            離開
          </button>
          <button type="button" className="bp-chat__albumBtn" aria-label="Album" onClick={() => requestOpenAlbum()}>
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" aria-hidden>
              <path
                d="M4 6.5C4 5.12 5.12 4 6.5 4h11C18.88 4 20 5.12 20 6.5v11c0 1.38-1.12 2.5-2.5 2.5h-11C5.12 20 4 18.88 4 17.5v-11Z"
                stroke="#5A5A5A"
                strokeWidth="1.6"
              />
              <path d="M8 10.5l2.2 2.4 2-1.9L17.5 16" stroke="#5A5A5A" strokeWidth="1.6" strokeLinejoin="round" />
              <path d="M9.2 9.2h.01" stroke="#5A5A5A" strokeWidth="3" strokeLinecap="round" />
            </svg>
          </button>
        </header>
      ) : null}

      <main className="bp-chat__thread">
        {messages.map((m, i) => {
          const self = isSelfMessage(m, localDisplayName)
          const rowClass =
            mineOnRight && self ? 'bp-chat__row bp-chat__row--self' : 'bp-chat__row bp-chat__row--other'
          return (
            <button
              type="button"
              key={`${m.messageId ?? 'noid'}-${m.timestamp ?? i}`}
              className={rowClass}
              onClick={() => {
                setReplyTarget(m)
                notifyReplySelect(m)
              }}
            >
              <span className="bp-chat__avatar" aria-hidden />
              <div className="bp-chat__col">
                <div className="bp-chat__sender">{displayNameOf(m)}</div>
                {m.replyToDisplayName && m.replyToMessagePreview ? (
                  <div className="bp-chat__replyCtx">
                    <span className="bp-chat__replyCtxName">{m.replyToDisplayName}</span>
                    <span className="bp-chat__replyCtxText">{m.replyToMessagePreview}</span>
                  </div>
                ) : null}
                <div className="bp-chat__bubble">{m.message}</div>
              </div>
            </button>
          )
        })}
        <div ref={bottomRef} />
      </main>

      {replyTarget ? (
        <div className="bp-chat__replyBar">
          <div className="bp-chat__replyBarText">
            Replying to {displayNameOf(replyTarget)}: {previewSnippet(replyTarget.message ?? '')}
          </div>
          <button
            type="button"
            className="bp-chat__replyBarX"
            onClick={() => {
              setReplyTarget(null)
              requestClearReply()
            }}
          >
            ×
          </button>
        </div>
      ) : null}

      {!useNativeComposer ? (
        <footer className="bp-chat__composer">
          <div className="bp-chat__composerInner">
            <textarea
              className="bp-chat__input bp-chat__input--textarea"
              placeholder="say something!"
              value={draft}
              rows={1}
              onChange={(e) => setDraft(e.target.value)}
              onCompositionStart={() => setIsComposing(true)}
              onCompositionEnd={() => setIsComposing(false)}
              onKeyDown={(e) => {
                // Important for IME: don't intercept Enter while composing.
                if (isComposing || (e.nativeEvent as unknown as { isComposing?: boolean }).isComposing) return
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault()
                  onSend()
                }
              }}
            />
            <button type="button" className="bp-chat__send" aria-label="Send" onClick={onSend}>
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" aria-hidden>
                <path
                  d="M3 11.5L21 4L13.5 21L11 13L3 11.5Z"
                  stroke="white"
                  strokeWidth="1.6"
                  strokeLinejoin="round"
                />
              </svg>
            </button>
          </div>
        </footer>
      ) : null}
    </div>
  )
}
