/** Mirrors Unity `ChatMessage` JSON from JsonUtility. */
export type ChatMessage = {
  messageId?: string
  userName?: string
  displayName?: string
  avatarId?: string
  message?: string
  timestamp?: number
  replyToMessageId?: string
  replyToDisplayName?: string
  replyToMessagePreview?: string
}

export type UnityChatPayload =
  | {
      kind: 'init'
      messages: ChatMessage[]
      roomName: string
      memberCount: number
      localDisplayName: string
      mineMessagesOnRight: boolean
      animalImageBase64?: string
      useNativeComposer?: boolean
    }
  | {
      kind: 'room'
      roomId: string
      localDisplayName?: string
      rooms?: RoomSummary[]
    }
  | { kind: 'append'; message: ChatMessage }
  | { kind: 'header'; roomName: string; memberCount: number }
  | { kind: 'clearReply' }
  | { kind: 'clearMessages' }

export type RoomSummary = {
  roomId: string
  name?: string
  lastActiveAt?: string
  petIndex?: number
  currentHealth?: number
  lastUpdateTime?: string
}

export function displayNameOf(m: ChatMessage): string {
  if (m.displayName && m.displayName.length > 0) return m.displayName
  return m.userName ?? ''
}
