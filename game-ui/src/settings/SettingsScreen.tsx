import { useState, useEffect, useCallback } from 'react'
import './settings-screen.css'

type SettingsPayload =
  | { kind: 'settings'; displayName: string; avatarBase64?: string }
  | { kind: 'avatarUpdated'; avatarBase64: string }
  | { kind: 'nicknameUpdated'; displayName: string }

function sendToUnity(obj: object) {
  const msg = JSON.stringify(obj)
  if (typeof window !== 'undefined' && (window as any).Unity?.call) {
    ;(window as any).Unity.call(msg)
  }
}

export function SettingsScreen() {
  const [displayName, setDisplayName] = useState('')
  const [avatarB64, setAvatarB64] = useState<string | null>(null)
  const [editingNickname, setEditingNickname] = useState(false)
  const [draft, setDraft] = useState('')
  const [status, setStatus] = useState<{ msg: string; type: 'ok' | 'err' | '' }>({ msg: '', type: '' })
  const [showConfirmDelete, setShowConfirmDelete] = useState(false)

  const applyPayload = useCallback((p: SettingsPayload) => {
    if (p.kind === 'settings') {
      setDisplayName(p.displayName ?? '')
      setAvatarB64(p.avatarBase64 && p.avatarBase64.length > 0 ? p.avatarBase64 : null)
    } else if (p.kind === 'avatarUpdated') {
      setAvatarB64(p.avatarBase64)
      showStatus('大頭貼已更新！', 'ok')
    } else if (p.kind === 'nicknameUpdated') {
      setDisplayName(p.displayName)
      setEditingNickname(false)
      showStatus('暱稱已更新！', 'ok')
    }
  }, [])

  useEffect(() => {
    const handler = (e: Event) => {
      const detail = (e as CustomEvent).detail as SettingsPayload
      if (detail) applyPayload(detail)
    }
    window.addEventListener('blockpet-settings', handler)
    sendToUnity({ type: 'ready' })
    return () => window.removeEventListener('blockpet-settings', handler)
  }, [applyPayload])

  function showStatus(msg: string, type: 'ok' | 'err') {
    setStatus({ msg, type })
    setTimeout(() => setStatus({ msg: '', type: '' }), 3000)
  }

  function handleChangeAvatar() {
    sendToUnity({ type: 'changeAvatar' })
  }

  function handleEditNickname() {
    setDraft(displayName)
    setEditingNickname(true)
  }

  function handleSaveNickname() {
    const name = draft.trim()
    if (!name) { showStatus('暱稱不能為空', 'err'); return }
    sendToUnity({ type: 'saveNickname', nickname: name })
  }

  function handleCancelEdit() {
    setEditingNickname(false)
    setDraft('')
  }

  function handleDeleteAccount() {
    setShowConfirmDelete(true)
  }

  function handleConfirmDelete() {
    setShowConfirmDelete(false)
    sendToUnity({ type: 'deleteAccount' })
  }

  function handleClose() {
    sendToUnity({ type: 'close' })
  }

  return (
    <div className="bp-settings">
      <header className="bp-settings__header">
        <span className="bp-settings__title">設定</span>
        <button className="bp-settings__closeBtn" onClick={handleClose}>✕</button>
      </header>

      <div className="bp-settings__body">
        {/* Avatar */}
        <div className="bp-settings__avatarSection">
          <div className="bp-settings__avatarWrap">
            {avatarB64
              ? <img className="bp-settings__avatarImg" src={`data:image/png;base64,${avatarB64}`} alt="avatar" />
              : <span className="bp-settings__avatarPlaceholder">🐾</span>
            }
          </div>
          <button className="bp-settings__changeAvatarBtn" onClick={handleChangeAvatar}>
            更換大頭貼
          </button>
        </div>

        {/* Nickname */}
        <div className="bp-settings__section">
          <div className="bp-settings__sectionTitle">帳號</div>
          {editingNickname ? (
            <div className="bp-settings__nicknameEdit">
              <input
                className="bp-settings__nicknameInput"
                value={draft}
                onChange={e => setDraft(e.target.value)}
                placeholder="輸入暱稱"
                maxLength={20}
              />
              <div className="bp-settings__nicknameActions">
                <button className="bp-settings__saveBtn" onClick={handleSaveNickname}>儲存</button>
                <button className="bp-settings__cancelBtn" onClick={handleCancelEdit}>取消</button>
              </div>
            </div>
          ) : (
            <button className="bp-settings__row" onClick={handleEditNickname}>
              <span className="bp-settings__rowLabel">暱稱</span>
              <span className="bp-settings__rowValue">{displayName || '未設定'}</span>
              <span className="bp-settings__rowArrow">›</span>
            </button>
          )}
        </div>

        {/* Status */}
        {status.msg ? (
          <p className={`bp-settings__status bp-settings__status--${status.type}`}>{status.msg}</p>
        ) : null}

        {/* Danger zone */}
        <div className="bp-settings__section">
          <div className="bp-settings__sectionTitle">帳號管理</div>
          <button className="bp-settings__row" onClick={() => sendToUnity({ type: 'logout' })}>
            <span className="bp-settings__rowLabel">登出</span>
            <span className="bp-settings__rowArrow">›</span>
          </button>
          <button className="bp-settings__row bp-settings__row--danger" onClick={handleDeleteAccount}>
            <span className="bp-settings__rowLabel">刪除帳號</span>
          </button>
        </div>
      </div>

      {/* Confirm delete overlay */}
      {showConfirmDelete && (
        <div className="bp-settings__confirmOverlay">
          <div className="bp-settings__confirmCard">
            <p className="bp-settings__confirmTitle">確定要刪除帳號？</p>
            <p className="bp-settings__confirmBody">此操作無法復原，所有資料將永久刪除。</p>
            <div className="bp-settings__confirmBtns">
              <button className="bp-settings__confirmDelete" onClick={handleConfirmDelete}>確定刪除</button>
              <button className="bp-settings__confirmCancel" onClick={() => setShowConfirmDelete(false)}>取消</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
