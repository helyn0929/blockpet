SH="filter: drop-shadow(0 1px 1.5px rgba(30,30,20,0.65)) drop-shadow(0 0 3px rgba(30,30,20,0.25))"
def svg(paths,size=28,extra=''):
    return f'<svg width="{size}" height="{size}" viewBox="0 0 24 24" fill="none" stroke="#FFFFFF" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" style="{SH}{extra}">{paths}</svg>'
I={
 'tent': '<path d="M3 19.5h18"/><path d="M5 19.5 12 6l7 13.5"/><path d="M10 19.5 12 14.5l2 5"/><path d="M12 6V3l3.2 1.3L12 5.6"/>',
 'house': '<path d="M4 11 12 4.5 20 11"/><path d="M6 9.5V19h12V9.5"/><path d="M10.5 19v-4.5h3V19"/>',
 'bed': '<path d="M3 14c0 3.3 4 5.5 9 5.5s9-2.2 9-5.5"/><path d="M3 14c0-1.7 4-3 9-3s9 1.3 9 3"/><path d="M5.5 12c.6-2.7 3.3-4.6 6.5-4.6s5.9 1.9 6.5 4.6"/>',
 'chev': '<path d="m6 9.5 6 6 6-6"/>',
 'pack': '<rect x="5.5" y="7.5" width="13" height="13" rx="4"/><path d="M9 7.5V5.5a3 3 0 0 1 6 0v2"/><path d="M8.5 13.5h7v3h-7z"/>',
 'paw': '<ellipse cx="12" cy="16" rx="4" ry="3.3"/><circle cx="5.8" cy="10.6" r="1.3"/><circle cx="9.3" cy="6.6" r="1.3"/><circle cx="14.7" cy="6.6" r="1.3"/><circle cx="18.2" cy="10.6" r="1.3"/>',
 'dots': '<circle cx="5.5" cy="12" r="1.2"/><circle cx="12" cy="12" r="1.2"/><circle cx="18.5" cy="12" r="1.2"/>',
 'polar': '<path d="M7.4 7.2 4.3 8 6.8 20.2l9.6-1.9"/><g transform="rotate(6 14.2 10.5)"><rect x="8.5" y="3.5" width="11.5" height="14" rx="1.8"/><rect x="10.2" y="5.2" width="8.1" height="7.6" rx=".8"/></g>',
 'book': '<path d="M4 5.5c2.5-1 5.5-1 8 .8 2.5-1.8 5.5-1.8 8-.8V19c-2.5-1-5.5-1-8 .8-2.5-1.8-5.5-1.8-8-.8Z"/><path d="M12 6.3v13.5"/><path d="m6.5 13.5 2-2.2 1.8 2"/>',
 'bubbles': '<path d="M3.5 6.5a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2H9l-3 2.5v-2.5h-.5a2 2 0 0 1-2-2Z"/><path d="M15.5 9h3a2 2 0 0 1 2 2v4.5a2 2 0 0 1-2 2H18v2.5l-3-2.5h-3.5a2 2 0 0 1-2-2v-.5"/>',
 'bubblepaw': '<path d="M4 6.5a2.5 2.5 0 0 1 2.5-2.5h11A2.5 2.5 0 0 1 20 6.5v8a2.5 2.5 0 0 1-2.5 2.5H11l-4.5 3.5V17A2.5 2.5 0 0 1 4 14.5Z"/><path d="M12 13.8s-3.2-1.9-3.2-3.9a1.7 1.7 0 0 1 3.2-.8 1.7 1.7 0 0 1 3.2.8c0 2-3.2 3.9-3.2 3.9Z"/>',
}
