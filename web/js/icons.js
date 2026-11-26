// The icons, for every page (the main window's and the note window's). A plain script placed first in
// <body>, so the symbols are there before anything uses them: <svg><use href="#i-name"/></svg>.
document.currentScript.insertAdjacentHTML('afterend', `<svg width="0" height="0" style="position:absolute" aria-hidden="true">
  <defs>
    <symbol id="i-logo" viewBox="0 0 24 24"><path d="M7.6 4.2 3.3 6.6a1 1 0 0 0-.4 1.3l4.5 8.4" opacity=".55"/><path d="m16.4 4.2 4.3 2.4a1 1 0 0 1 .4 1.3l-4.5 8.4" opacity=".55"/><rect x="6.5" y="3" width="11" height="13" rx="1.8"/><path d="M6.5 6.6h11M9 10h6M9 12.8h4"/><circle cx="12" cy="19.4" r="2"/></symbol>
    <symbol id="i-collapse" viewBox="0 0 24 24"><path d="m14.5 6-6 6 6 6"/></symbol>
    <symbol id="i-expand" viewBox="0 0 24 24"><path d="m9.5 6 6 6-6 6"/></symbol>
    <symbol id="i-plus" viewBox="0 0 24 24"><path d="M12 5v14M5 12h14"/></symbol>
    <symbol id="i-search" viewBox="0 0 24 24"><circle cx="10.5" cy="10.5" r="6.5"/><path d="m20 20-4.8-4.8"/></symbol>
    <symbol id="i-up" viewBox="0 0 24 24"><path d="m6 15 6-6 6 6"/></symbol>
    <symbol id="i-down" viewBox="0 0 24 24"><path d="m6 9 6 6 6-6"/></symbol>
    <symbol id="i-sound" viewBox="0 0 24 24"><path d="M11 5 6 9H3v6h3l5 4z"/><path d="M15.5 8.5a5 5 0 0 1 0 7M18.5 5.5a9 9 0 0 1 0 13"/></symbol>
    <symbol id="i-mute" viewBox="0 0 24 24"><path d="M11 5 6 9H3v6h3l5 4z"/><path d="m16 9 5 6M21 9l-5 6"/></symbol>
    <symbol id="i-reset" viewBox="0 0 24 24"><path d="M4 12a8 8 0 1 0 2.6-5.9"/><path d="M4 4.5V9h4.5"/></symbol>
    <symbol id="i-settings" viewBox="0 0 24 24"><path d="M4 7h9M17 7h3M4 17h3M11 17h9"/><circle cx="15" cy="7" r="2.2"/><circle cx="9" cy="17" r="2.2"/></symbol>
    <symbol id="i-tag" viewBox="0 0 24 24"><path d="M3.5 12.2V4.5a1 1 0 0 1 1-1h7.7l8.3 8.3a1 1 0 0 1 0 1.4l-7.4 7.4a1 1 0 0 1-1.4 0z"/><circle cx="8" cy="8" r="1.4"/></symbol>
    <symbol id="i-cards" viewBox="0 0 24 24"><path d="M8 4.5h10.5A1.5 1.5 0 0 1 20 6v9"/><rect x="4" y="8" width="13" height="11.5" rx="1.6"/><path d="M4 11.8h13"/></symbol>
    <symbol id="i-grid" viewBox="0 0 24 24"><rect x="4" y="4" width="6.5" height="6.5" rx="1.4"/><rect x="13.5" y="4" width="6.5" height="6.5" rx="1.4"/><rect x="4" y="13.5" width="6.5" height="6.5" rx="1.4"/><rect x="13.5" y="13.5" width="6.5" height="6.5" rx="1.4"/></symbol>
    <symbol id="i-book" viewBox="0 0 24 24"><path d="M5 4.5A1.5 1.5 0 0 1 6.5 3H19v15H6.5A1.5 1.5 0 0 0 5 19.5z"/><path d="M5 19.5A1.5 1.5 0 0 0 6.5 21H19v-3"/></symbol>
    <symbol id="i-caret" viewBox="0 0 24 24"><path d="m7 10 5 5 5-5"/></symbol>
    <symbol id="i-back" viewBox="0 0 24 24"><path d="m14 6-6 6 6 6"/></symbol>
    <symbol id="i-pencil" viewBox="0 0 24 24"><path d="M4.5 19.5 5.3 15.6 15.6 5.3a2 2 0 0 1 2.9 0l.2.2a2 2 0 0 1 0 2.9L8.4 18.7z"/><path d="m13.8 7.1 3.1 3.1M12.5 19.5h7"/></symbol>
    <symbol id="i-lock" viewBox="0 0 24 24"><rect x="5" y="10.5" width="14" height="10" rx="2"/><path d="M8 10.5V8a4 4 0 0 1 8 0v2.5"/></symbol>
    <symbol id="i-trash" viewBox="0 0 24 24"><path d="M4 7h16M10 3.5h4M6.5 7l1 13h9l1-13M10 11v6M14 11v6"/></symbol>
    <symbol id="i-x" viewBox="0 0 24 24"><path d="M6 6l12 12M18 6 6 18"/></symbol>
    <symbol id="i-toolbar" viewBox="0 0 24 24"><rect x="3.5" y="4.5" width="17" height="15" rx="2"/><path d="M3.5 9h17M9 9v10.5"/></symbol>
    <symbol id="i-minimize" viewBox="0 0 24 24"><path d="M6 12.5h12"/></symbol>
    <symbol id="i-bold" viewBox="0 0 24 24"><path d="M7.5 4.5h5.2a3.6 3.6 0 0 1 0 7.2H7.5zM7.5 11.7h6.1a3.9 3.9 0 0 1 0 7.8H7.5z" stroke-width="2.6"/></symbol>
    <symbol id="i-italic" viewBox="0 0 24 24"><path d="M10.5 4.5h7M6.5 19.5h7M14.5 4.5l-5 15"/></symbol>
    <symbol id="i-underline" viewBox="0 0 24 24"><path d="M7.5 4.5v6.5a4.5 4.5 0 0 0 9 0V4.5M5.5 20h13"/></symbol>
    <symbol id="i-strike" viewBox="0 0 24 24"><path d="M16.3 7.2C15.5 5.6 14 4.6 12 4.6c-2.6 0-4.4 1.4-4.4 3.4 0 1.5.9 2.5 2.9 3.2M4.5 12h15M15.3 14.3c.5.6.8 1.2.8 2 0 2.1-1.8 3.4-4.3 3.4-2.1 0-3.7-.9-4.6-2.4"/></symbol>
    <symbol id="i-ul" viewBox="0 0 24 24"><circle cx="5" cy="7" r="1.3" fill="currentColor"/><circle cx="5" cy="12" r="1.3" fill="currentColor"/><circle cx="5" cy="17" r="1.3" fill="currentColor"/><path d="M9.5 7H20M9.5 12H20M9.5 17H20"/></symbol>
    <symbol id="i-ol" viewBox="0 0 24 24"><path d="M3.8 5.2 5 4.5v5M3.8 9.5h2.4M3.6 14.6a1.3 1.3 0 1 1 2.2 1L3.6 19h2.7M10 7h10M10 12h10M10 17h10"/></symbol>
    <symbol id="i-check" viewBox="0 0 24 24"><rect x="3" y="4" width="6" height="6" rx="1.2"/><path d="m4.4 7.1 1.3 1.3L8.2 5.6"/><rect x="3" y="14" width="6" height="6" rx="1.2"/><path d="M12 7h8M12 17h8"/></symbol>
    <symbol id="i-indent" viewBox="0 0 24 24"><path d="M10 6h10M10 12h10M10 18h10M3.5 9l3 3-3 3"/></symbol>
    <symbol id="i-outdent" viewBox="0 0 24 24"><path d="M10 6h10M10 12h10M10 18h10M6.5 9l-3 3 3 3"/></symbol>
    <symbol id="i-popout" viewBox="0 0 24 24"><path d="M13.5 4H20v6.5M20 4l-8.5 8.5"/><path d="M18 14v4.5a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 4 18.5v-11A1.5 1.5 0 0 1 5.5 6H10"/></symbol>
    <symbol id="i-popin" viewBox="0 0 24 24"><path d="M20 4l-8.5 8.5M11.5 6.5v6h6"/><path d="M18 14v4.5a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 4 18.5v-11A1.5 1.5 0 0 1 5.5 6H10"/></symbol>
    <symbol id="i-pin" viewBox="0 0 24 24"><path d="M9 3.5h6l-1 6.2 3.2 3.3H6.8L10 9.7z"/><path d="M12 13v7.5"/></symbol>
  </defs>
</svg>`);
