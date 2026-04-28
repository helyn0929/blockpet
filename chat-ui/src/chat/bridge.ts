import type { ChatMessage } from './types'
import { displayNameOf } from './types'

declare global {
  interface Window {
    Unity?: { call: (msg: string) => void }
  }
}

export function sendToUnity(payload: {
  type:
    | 'ready'
    | 'send'
    | 'back'
    | 'clearReply'
    | 'openAlbum'
    | 'leaveChat'
    | 'replySelect'
    | 'setRoom'
    | 'createRoom'
    | 'joinRoom'
    | 'refreshRooms'
  text?: string
  replyToMessageId?: string
  replyToDisplayName?: string
  replyToMessagePreview?: string
  selectedMessageId?: string
  selectedUserName?: string
  selectedDisplayName?: string
  selectedMessageBody?: string
  roomId?: string
  roomName?: string
}): void {
  const json = JSON.stringify(payload)
  if (window.Unity && typeof window.Unity.call === 'function') {
    window.Unity.call(json)
  } else {
    console.debug('[blockpet-chat] (no Unity) ', json)
  }
}

/** Called once after React has mounted and registered event listeners. */
export function notifyReady(): void {
  sendToUnity({ type: 'ready' })
}

export function sendMessage(
  text: string,
  replyTarget: ChatMessage | null,
  previewSnippet: (s: string) => string,
): void {
  const trimmed = text.trim()
  if (!trimmed) return
  if (replyTarget && replyTarget.messageId) {
    sendToUnity({
      type: 'send',
      text: trimmed,
      replyToMessageId: replyTarget.messageId,
      replyToDisplayName: replyTarget.displayName || replyTarget.userName || '',
      replyToMessagePreview: previewSnippet(replyTarget.message ?? ''),
    })
  } else {
    sendToUnity({ type: 'send', text: trimmed })
  }
}

export function requestBack(): void {
  sendToUnity({ type: 'back' })
}

export function requestClearReply(): void {
  sendToUnity({ type: 'clearReply' })
}

export function requestOpenAlbum(): void {
  sendToUnity({ type: 'openAlbum' })
}

/** Stops Firebase chat listener and returns home (Unity `WebViewRequestLeaveChat`). */
export function requestLeaveChat(): void {
  sendToUnity({ type: 'leaveChat' })
}

/** Keeps Unity reply target in sync when tapping a message (or clears when target is null / no id). */
export function notifyReplySelect(target: ChatMessage | null): void {
  if (!target?.messageId) {
    sendToUnity({ type: 'replySelect', selectedMessageId: '' })
    return
  }
  sendToUnity({
    type: 'replySelect',
    selectedMessageId: target.messageId,
    selectedUserName: target.userName ?? '',
    selectedDisplayName: displayNameOf(target),
    selectedMessageBody: target.message ?? '',
  })
}

export function requestSetRoom(roomId: string): void {
  const trimmed = roomId.trim()
  if (!trimmed) return
  sendToUnity({ type: 'setRoom', roomId: trimmed })
}

export function requestCreateRoom(roomId: string, roomName?: string): void {
  const trimmed = roomId.trim()
  if (!trimmed) return
  sendToUnity({ type: 'createRoom', roomId: trimmed, roomName })
}

export function requestJoinRoom(roomId: string): void {
  const trimmed = roomId.trim()
  if (!trimmed) return
  sendToUnity({ type: 'joinRoom', roomId: trimmed })
}

export function requestRefreshRooms(): void {
  sendToUnity({ type: 'refreshRooms' })
}
