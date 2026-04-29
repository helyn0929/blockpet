import { useState, useEffect, useCallback } from 'react'
import './market-screen.css'

type Category = 'Pets' | 'Accessories' | 'Furnitures' | 'Backgrounds' | 'Spaces'

type MarketItem = {
  id: string
  name: string
  category: Category
  section: string
  price: number
  gemPrice: number
  isOwned: boolean
  isEquipped: boolean
  isLocked: boolean
  iconBase64: string
}

type MarketPayload = {
  kind: 'market'
  items: MarketItem[]
  coins: number
  gems: number
  equippedPetId: string
}

const CATEGORIES: Category[] = ['Pets', 'Accessories', 'Furnitures', 'Backgrounds', 'Spaces']
const CATEGORY_LABELS: Record<Category, string> = {
  Pets: '寵物',
  Accessories: '飾品',
  Furnitures: '傢俱',
  Backgrounds: '背景',
  Spaces: '空間',
}

function sendMarket(type: string, itemId?: string) {
  const msg = itemId ? JSON.stringify({ type, itemId }) : JSON.stringify({ type })
  if (window.Unity && typeof window.Unity.call === 'function') {
    window.Unity.call(msg)
  } else {
    console.debug('[market]', msg)
  }
}

export function MarketScreen() {
  const [items, setItems] = useState<MarketItem[]>([])
  const [coins, setCoins] = useState(0)
  const [gems, setGems] = useState(0)
  const [equippedPetId, setEquippedPetId] = useState('')
  const [activeCategory, setActiveCategory] = useState<Category>('Pets')
  const [selectedItem, setSelectedItem] = useState<MarketItem | null>(null)

  // Client-side try-on preview (separate from equipped state)
  const [previewPetId, setPreviewPetId] = useState<string | null>(null)
  const [previewBgId, setPreviewBgId] = useState<string | null>(null)
  const [previewAccessoryIds, setPreviewAccessoryIds] = useState<string[]>([])
  const [previewFurnitureId, setPreviewFurnitureId] = useState<string | null>(null)

  const getItem = useCallback((id: string | null): MarketItem | null => {
    if (!id) return null
    return items.find(i => i.id === id) ?? null
  }, [items])

  useEffect(() => {
    const handler = (e: Event) => {
      const p = (e as CustomEvent).detail as MarketPayload
      if (!p || p.kind !== 'market') return
      setItems(p.items ?? [])
      setCoins(p.coins ?? 0)
      setGems(p.gems ?? 0)
      setEquippedPetId(p.equippedPetId ?? '')
    }
    window.addEventListener('blockpet-chat', handler)
    return () => window.removeEventListener('blockpet-chat', handler)
  }, [])

  // Reset preview on category switch
  useEffect(() => {
    setSelectedItem(null)
    if (activeCategory === 'Pets') {
      setPreviewPetId(null)
    }
    if (activeCategory !== 'Accessories') {
      setPreviewAccessoryIds([])
    }
    if (activeCategory !== 'Furnitures') {
      setPreviewFurnitureId(null)
    }
    if (activeCategory !== 'Backgrounds') {
      setPreviewBgId(null)
    }
  }, [activeCategory])

  function handleItemTap(item: MarketItem) {
    setSelectedItem(item)
    switch (item.category) {
      case 'Pets':
        setPreviewPetId(item.id)
        break
      case 'Backgrounds':
        setPreviewBgId(item.id)
        break
      case 'Accessories':
        setPreviewAccessoryIds(prev => {
          if (prev.includes(item.id)) return prev
          return [...prev, item.id].slice(-4)
        })
        break
      case 'Furnitures':
        setPreviewFurnitureId(item.id)
        break
    }
  }

  // Derive preview images
  const equippedBgItem = items.find(i => i.category === 'Backgrounds' && i.isEquipped)
  const previewPetItem = getItem(previewPetId) ?? items.find(i => i.id === equippedPetId)
  const previewBgItem = getItem(previewBgId) ?? equippedBgItem
  const previewFurnitureItem = getItem(previewFurnitureId)

  // Group filtered items by section
  const filteredItems = items.filter(i => i.category === activeCategory)
  const grouped: { section: string; items: MarketItem[] }[] = []
  for (const item of filteredItems) {
    const sec = item.section || ''
    const last = grouped[grouped.length - 1]
    if (!last || last.section !== sec) grouped.push({ section: sec, items: [item] })
    else last.items.push(item)
  }

  return (
    <div className="bp-market">
      <div className="bp-market__header">
        <button className="bp-market__back" onClick={() => sendMarket('back')}>‹</button>
        <span className="bp-market__title">市集</span>
        <div className="bp-market__currency">
          <span className="bp-market__coin">🪙 {coins}</span>
          <span className="bp-market__gem">💎 {gems}</span>
        </div>
      </div>

      <div className="bp-market__preview">
        {previewBgItem?.iconBase64 && (
          <img className="bp-market__layer bp-market__layer--bg"
            src={`data:image/jpeg;base64,${previewBgItem.iconBase64}`} alt="" />
        )}
        {previewPetItem?.iconBase64 && (
          <img className="bp-market__layer bp-market__layer--pet"
            src={`data:image/jpeg;base64,${previewPetItem.iconBase64}`} alt="" />
        )}
        {previewAccessoryIds.map(id => {
          const acc = getItem(id)
          if (!acc?.iconBase64) return null
          return (
            <img key={id} className="bp-market__layer bp-market__layer--accessory"
              src={`data:image/jpeg;base64,${acc.iconBase64}`} alt="" />
          )
        })}
        {previewFurnitureItem?.iconBase64 && (
          <img className="bp-market__layer bp-market__layer--furniture"
            src={`data:image/jpeg;base64,${previewFurnitureItem.iconBase64}`} alt="" />
        )}
        {!previewPetItem && <div className="bp-market__preview-empty">🐾</div>}
        {previewAccessoryIds.length > 0 && (
          <button className="bp-market__preview-clear" onClick={() => setPreviewAccessoryIds([])}>✕</button>
        )}
      </div>

      <div className="bp-market__tabs">
        {CATEGORIES.map(cat => (
          <button
            key={cat}
            className={`bp-market__tab${activeCategory === cat ? ' bp-market__tab--active' : ''}`}
            onClick={() => setActiveCategory(cat)}
          >
            {CATEGORY_LABELS[cat]}
          </button>
        ))}
      </div>

      <div className="bp-market__grid-scroll">
        {grouped.map(group => (
          <div key={group.section} className="bp-market__section">
            {group.section && <div className="bp-market__section-label">{group.section}</div>}
            <div className="bp-market__grid">
              {group.items.map(item => (
                <ItemCard
                  key={item.id}
                  item={item}
                  selected={selectedItem?.id === item.id}
                  onTap={handleItemTap}
                />
              ))}
            </div>
          </div>
        ))}
      </div>

      {selectedItem && !selectedItem.isLocked && (
        <div className="bp-market__action-bar">
          <span className="bp-market__action-name">{selectedItem.name}</span>
          {selectedItem.isOwned ? (
            <button
              className={`bp-market__action-btn bp-market__action-btn--equip${selectedItem.isEquipped ? ' bp-market__action-btn--equipped' : ''}`}
              onClick={() => !selectedItem.isEquipped && sendMarket('equipItem', selectedItem.id)}
            >
              {selectedItem.isEquipped ? '已裝備' : '裝備'}
            </button>
          ) : (
            <button
              className="bp-market__action-btn bp-market__action-btn--buy"
              onClick={() => sendMarket('buyItem', selectedItem.id)}
            >
              {selectedItem.price > 0
                ? `🪙 ${selectedItem.price}`
                : selectedItem.gemPrice > 0
                  ? `💎 ${selectedItem.gemPrice}`
                  : '免費獲取'}
            </button>
          )}
        </div>
      )}
    </div>
  )
}

function ItemCard({
  item,
  selected,
  onTap,
}: {
  item: MarketItem
  selected: boolean
  onTap: (i: MarketItem) => void
}) {
  let badge: React.ReactNode
  let extraClass = ''

  if (item.isLocked) {
    badge = <span className="bp-market__badge bp-market__badge--locked">🔒</span>
    extraClass = ' bp-market__card--locked'
  } else if (item.isEquipped) {
    badge = <span className="bp-market__badge bp-market__badge--equipped">裝備中</span>
    extraClass = ' bp-market__card--equipped'
  } else if (item.isOwned) {
    badge = <span className="bp-market__badge bp-market__badge--owned">已擁有</span>
    extraClass = ' bp-market__card--owned'
  } else {
    const label = item.price > 0
      ? `🪙${item.price}`
      : item.gemPrice > 0
        ? `💎${item.gemPrice}`
        : '免費'
    badge = <span className="bp-market__badge bp-market__badge--price">{label}</span>
  }

  return (
    <button
      className={`bp-market__card${extraClass}${selected ? ' bp-market__card--selected' : ''}`}
      onClick={() => onTap(item)}
    >
      <div className="bp-market__card-img-wrap">
        {item.iconBase64 ? (
          <img src={`data:image/jpeg;base64,${item.iconBase64}`} alt={item.name} className="bp-market__card-img" />
        ) : (
          <div className="bp-market__card-no-img">🐾</div>
        )}
        {badge}
      </div>
      <div className="bp-market__card-name">{item.name}</div>
    </button>
  )
}
