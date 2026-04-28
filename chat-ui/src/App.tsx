import { useEffect, useMemo, useState } from 'react'
import type { RoomSummary, UnityChatPayload } from './chat/types'
import { notifyReady } from './chat/bridge'
import { ChatScreen } from './chat/ChatScreen'
import { RoomScreen } from './room/RoomScreen'

function App() {
  const devScreen = useMemo(() => {
    try {
      const v = new URLSearchParams(window.location.search).get('screen')
      if (v === 'chat' || v === 'room') return v as 'chat' | 'room'
    } catch {
      // ignore
    }
    return null
  }, [])

  const [mode, setMode] = useState<'room' | 'chat'>(() => {
    // URL param ?screen=chat|room works in both Unity and dev.
    // Default to 'room' so RoomWebViewBridge shows the room-select UI immediately
    // without a chat-screen flash. ChatWebViewBridge passes ?screen=chat in its URL.
    if (devScreen) return devScreen
    return 'room'
  })

  const [chatInit, setChatInit] = useState<Extract<UnityChatPayload, { kind: 'init' }>>({
    kind: 'init',
    messages: [],
    roomName: 'Chatroom (0)',
    memberCount: 0,
    localDisplayName: 'Guest',
    mineMessagesOnRight: true,
    animalImageBase64: undefined,
    useNativeComposer: false,
  })

  const [currentRoomId, setCurrentRoomId] = useState<string>('')

  const [rooms, setRooms] = useState<RoomSummary[]>(() => {
    if (window.Unity) return []
    // Mock rooms for dev preview.
    return [
      { roomId: 'ABC123', name: 'Room ABC123', petIndex: 2, currentHealth: 65000 },
      { roomId: 'K9P7Q2', name: 'Room K9P7Q2', petIndex: 6, currentHealth: 32000 },
      { roomId: 'DOG888', name: 'Dog Room', petIndex: 1, currentHealth: 82000 },
      { roomId: 'CAT222', name: 'Cat Room', petIndex: 8, currentHealth: 41000 },
    ]
  })

  const roomTitle = useMemo(() => {
    return '選擇房間'
  }, [])

  useEffect(() => {
    // Dev-only: allow switching without Unity by changing URL and reloading.
    if (!window.Unity && devScreen)
      setMode(devScreen)

    const onUnity = (e: Event) => {
      const ce = e as CustomEvent<unknown>
      const p = ce.detail as UnityChatPayload
      if (!p || typeof p !== 'object' || !('kind' in p)) return

      if (p.kind === 'room') {
        setMode('room')
        setRooms(p.rooms ?? [])
        if (p.roomId) setCurrentRoomId(p.roomId)
        return
      }

      if (p.kind === 'init') {
        setMode('chat')
        setChatInit(p)
      }
    }

    window.addEventListener('blockpet-chat', onUnity)
    // Handshake so Unity knows the listener is attached (both Room + Chat pages rely on this).
    notifyReady()
    return () => window.removeEventListener('blockpet-chat', onUnity)
  }, [devScreen])

  return mode === 'room' ? <RoomScreen title={roomTitle} rooms={rooms} currentRoomId={currentRoomId} /> : <ChatScreen init={chatInit} />
}

export default App
