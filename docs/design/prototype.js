'use strict';
const statuses = ['Not started', 'Playing', 'Completed', 'On hold'];
const root = 'D:\\GameVault';
const games = [
  {id:'re2',name:'Resident Evil 2',art:'re',mark:'2',tag:'Return to Raccoon City',status:'Playing',minutes:1234,last:'Today',exe:'D:\\Games\\Resident Evil 2\\re2.exe',working:'D:\\Games\\Resident Evil 2',save:'C:\\Users\\Player\\Saved Games\\RE2',retention:20,keepAll:false,sessions:[['Today, 19:42','1h 24m'],['Yesterday, 20:10','2h 08m'],['8 Nov, 18:30','48m']]},
  {id:'vale',name:'Aster Vale',art:'vale',mark:'✦',tag:'A world beyond the canopy',status:'Completed',minutes:2456,last:'Yesterday',exe:'D:\\Games\\Aster Vale\\game.exe',working:'D:\\Games\\Aster Vale',save:'C:\\Users\\Player\\Saved Games\\AsterVale',retention:20,keepAll:false,sessions:[['Yesterday, 18:00','56m']]},
  {id:'signal',name:'Signal Lost',art:'signal',mark:'∿',tag:'Somewhere, a signal remains',status:'Not started',minutes:0,last:'Never',exe:'D:\\Games\\Signal Lost\\game.exe',working:'D:\\Games\\Signal Lost',save:'',retention:20,keepAll:false,sessions:[]}
];
for (const game of games) game.backups = game.save ? [{id:'b1',date:'Today, 21:06',size:'14.2 MB',pinned:false},{id:'b2',date:'Yesterday, 22:18',size:'14.1 MB',pinned:true},{id:'b3',date:'8 Nov, 19:18',size:'13.8 MB',pinned:false}] : [];
const state = {view:'library',selected:'re2',tab:'overview',filter:'All',query:'',preview:'normal',period:'Week',theme:'midnight',retention:20,keepAll:false,reduced:false,scroll:0};
const content = document.getElementById('content');
const main = document.getElementById('main');
const modal = document.getElementById('modal');
const form = document.getElementById('modal-form');
let formContext = null, opener = null, noticeTimer = null, ticker = null, sessionStarted = 0;
function escapeHtml(value) { const el=document.createElement('span'); el.textContent=String(value); return el.innerHTML.replace(/"/g,'&quot;'); }
function duration(minutes) { return minutes ? `${Math.floor(minutes/60)}h ${minutes%60}m` : '0h'; }
function currentGame() { return games.find(g=>g.id===state.selected) || games[0]; }
function busy(game) { return Boolean(game.running || game.backing || state.preview==='running' || state.preview==='backing'); }
function cover(game) { return `<div class="cover ${game.art}" aria-hidden="true"><span class="poster-tag">${escapeHtml(game.tag)}</span><span class="poster-mark">${game.mark}</span><span class="poster-title">${escapeHtml(game.name)}</span></div>`; }
function backupStatus(game) {
  if(state.preview==='nosaves'||!game.save) return '<span class="warning">Save location needed</span>';
  if(game.backing||state.preview==='backing') return '<span>Backing up…</span>';
  if(state.preview==='failed') return '<span class="warning">Backup failed · retry needed</span>';
  return game.backups.length ? '<span class="good">Saves protected</span>' : '<span class="warning">No backup yet</span>';
}
function elapsed() { const s=Math.floor((Date.now()-sessionStarted)/1000); return `${String(Math.floor(s/3600)).padStart(2,'0')}:${String(Math.floor(s/60)%60).padStart(2,'0')}:${String(s%60).padStart(2,'0')}`; }
function notify(message) { const box=document.getElementById('notice'); box.textContent=message; clearTimeout(noticeTimer); noticeTimer=setTimeout(()=>{box.textContent='';},6000); }
function render() {
  const focusId=document.activeElement?.id;
  document.querySelectorAll('nav button').forEach(b=>b.setAttribute('aria-current',b.dataset.nav===state.view?'page':'false'));
  const titles={library:['Your collection','Library'],details:['Your collection',currentGame().name],activity:['Your time, at a glance','Activity'],settings:['Make it yours','Settings']};
  document.getElementById('context').textContent=titles[state.view][0];
  document.getElementById('heading').textContent=titles[state.view][1];
  document.getElementById('search-wrap').hidden=state.view!=='library';
  content.innerHTML=state.view==='library'?libraryView():state.view==='details'?detailsView():state.view==='activity'?activityView():settingsView();
  if(focusId) document.getElementById(focusId)?.focus({preventScroll:true});
}
function navigate(view) { if(state.view==='library') state.scroll=window.scrollY; state.view=view; render(); window.scrollTo(0,view==='library'?state.scroll:0); }
function libraryView() {
  if(state.preview==='loading') return '<div class="empty" role="status"><h2>Loading your library</h2><p>Your profiles are staying on your device.</p></div>';
  if(state.preview==='empty') return '<div class="empty"><h2>A home for every game</h2><p>Add a game to start tracking your time and keeping its progress safe.</p><button class="primary" data-action="add">Add your first game</button></div>';
  const shown=games.filter(g=>(state.filter==='All'||g.status===state.filter)&&g.name.toLowerCase().includes(state.query.toLowerCase()));
  return `<div class="filters" aria-label="Filter games">${['All',...statuses].map(s=>`<button id="filter-${s.replaceAll(' ','-')}" data-filter="${s}" aria-pressed="${state.filter===s}">${s}</button>`).join('')}</div>
    <div class="summary-line"><span>${shown.length} ${shown.length===1?'game':'games'} in this view</span><span>Latest activity stays with each profile</span></div>
    ${shown.length?`<div class="games">${shown.map(g=>`<button class="game-card" data-game="${g.id}" aria-label="Open ${escapeHtml(g.name)}">${cover(g)}<h2>${escapeHtml(g.name)}</h2><div class="game-meta"><span>${duration(g.minutes)}</span><span class="pill">${g.running?'Running':g.status}</span></div><p>${backupStatus(g)}</p></button>`).join('')}</div>`:'<div class="empty"><h2>No matching games</h2><p>Try another search or choose All.</p></div>'}
    <p class="preview-tip">Resident Evil 2 is the initial reference game. Other titles and all artwork are illustrative placeholders.</p>`;
}
function callout(game) {
  if(state.preview==='missing') return '<div class="callout warn"><div><strong>Game not found</strong><p>The executable has moved or was removed. Your history and backups are still here.</p></div><button data-action="edit">Edit game</button></div>';
  if(state.preview==='failed') return `<div class="callout warn" role="alert"><div><strong>Backup could not finish</strong><p>Example error: the destination drive is full. Free some space and retry. Last successful backup: ${escapeHtml(game.backups[0]?.date||'None')}.</p></div><button data-action="retry" ${busy(game)?'disabled':''}>Retry backup</button></div>`;
  if(!game.save||state.preview==='nosaves') return '<div class="callout warn"><div><strong>Save location needed</strong><p>You can play now. Choose where this game saves before creating a backup.</p></div><button data-action="edit">Set save location</button></div>';
  return `<div class="callout"><div><strong>${backupStatus(game)}</strong><p>Last successful backup: ${escapeHtml(game.backups[0]?.date||'None yet')}. Kept in this game’s local profile.</p></div><span class="muted">${game.backups.length} backups</span></div>`;
}
function detailsView() {
  const g=currentGame(), running=g.running||state.preview==='running', blocked=busy(g)||state.preview==='missing';
  return `<button class="quiet back" data-nav="library">← Back to library</button><div class="detail-hero">${cover(g)}<div><div class="detail-title"><h2>${escapeHtml(g.name)}</h2><label><span class="sr-only">Game status</span><select id="game-status">${statuses.map(s=>`<option ${s===g.status?'selected':''}>${s}</option>`).join('')}</select></label></div>
    <div class="metrics"><div class="metric"><strong>${duration(g.minutes)}</strong><span>Total playtime</span></div><div class="metric"><strong>${escapeHtml(g.last)}</strong><span>Last played</span></div><div class="metric"><strong data-elapsed>${g.running?elapsed():running?'00:24:18':'—'}</strong><span>Current session</span></div></div>
    <div class="play-row"><button class="primary" data-action="play" ${blocked?'disabled':''}>${running?'Demo session running':g.backing||state.preview==='backing'?'Backing up…':'▶ Play'}</button>${g.running?'<button data-action="end">End demo session</button>':''}<button class="quiet" data-action="edit">Edit game</button></div></div></div>${callout(g)}
    <div class="tabs" role="tablist" aria-label="Game details">${['overview','backups'].map(t=>`<button id="tab-${t}" role="tab" aria-controls="detail-panel" data-tab="${t}" aria-selected="${state.tab===t}" tabindex="${state.tab===t?'0':'-1'}">${t==='overview'?'Overview':'Backups'}</button>`).join('')}</div>
    <section id="detail-panel" role="tabpanel" aria-labelledby="tab-${state.tab}">${state.tab==='backups'?backupsView(g):overviewView(g)}</section>`;
}
function overviewView(g) { return `<section class="panel"><div class="section-head"><h3>Recent sessions</h3><button class="quiet small" data-action="correct">Correct playtime</button></div>${g.sessions.length?`<div class="table-wrap"><table><thead><tr><th scope="col">Session</th><th scope="col">Time played</th></tr></thead><tbody>${g.sessions.map(s=>`<tr><td>${escapeHtml(s[0])}</td><td>${escapeHtml(s[1])}</td></tr>`).join('')}</tbody></table></div>`:'<p class="muted">Your sessions will appear after you play.</p>'}</section><section class="panel"><h3>Save protection</h3><p class="path">${escapeHtml(g.save||'No save location configured')}</p><p class="muted">Keep ${g.keepAll?'every backup':`the latest ${g.retention} unpinned backups`}, plus all pinned backups.</p><button data-action="show-backups">Manage backups</button></section>`; }
function backupsView(g) { return `<section class="panel"><div class="section-head"><div><h3>Backup history</h3><small>Newest first · ${g.keepAll?'Keep everything':`${g.retention} unpinned + all pinned`}</small></div><button data-action="backup" ${busy(g)||!g.save||state.preview==='nosaves'?'disabled':''}>Back up now</button></div>${g.backups.length?`<div class="table-wrap"><table><thead><tr><th scope="col">Created</th><th scope="col">Size</th><th scope="col">Actions</th></tr></thead><tbody>${g.backups.map(b=>`<tr><td>${escapeHtml(b.date)}${b.recovery?' <span class="pill">Before restore</span>':''}</td><td>${b.size}</td><td class="backup-actions"><button id="pin-${b.id}" class="small quiet" data-pin="${b.id}" aria-pressed="${b.pinned}" aria-label="${b.pinned?'Unpin':'Pin'} backup ${escapeHtml(b.date)}">${b.pinned?'Pinned':'Pin'}</button><button class="small" data-restore="${b.id}" ${busy(g)||!g.save||state.preview==='nosaves'?'disabled':''}>Restore</button></td></tr>`).join('')}</tbody></table></div>`:'<p class="muted">No verified backups yet. Configure a save location and create your first backup.</p>'}${busy(g)?'<p class="warning">Backup and restore are unavailable while the game is running or another save operation is active.</p>':''}</section>`; }
function activityView() {
  const data=state.period==='Day'?[0,0,24,40,0,54,26]:state.period==='Month'?[180,80,145,210,0,140,90]:[42,76,0,118,54,0,84];
  return `<div class="filters" aria-label="Activity period">${['Day','Week','Month'].map(p=>`<button id="period-${p}" data-period="${p}" aria-pressed="${state.period===p}">${p}</button>`).join('')}</div><div class="metrics"><div class="metric"><strong>${duration(data.reduce((a,b)=>a+b,0))}</strong><span>This ${state.period.toLowerCase()}</span></div><div class="metric"><strong>Resident Evil 2</strong><span>Most played · example data</span></div></div><section class="panel"><h3>Time spent playing</h3><p class="muted">${state.period==='Week'?'This week':state.period==='Day'?'Today, grouped into time blocks':'This month, grouped into date ranges'} · values shown in minutes</p><div class="chart" role="img" aria-label="${data.map((n,i)=>`${i+1}: ${n} minutes`).join(', ')}">${data.map((n,i)=>`<div class="bar-column"><span class="bar-value">${n}m</span><div class="bar" style="--height:${Math.round(n/Math.max(...data)*145)}px"></div>${state.period==='Week'?['Mon','Tue','Wed','Thu','Fri','Sat','Sun'][i]:state.period==='Day'?['00–04','04–08','08–12','12–16','16–20','20–22','22–24'][i]:['1–4','5–8','9–12','13–16','17–20','21–24','25–30'][i]}</div>`).join('')}</div></section>`;
}
function settingsView() { return `<section class="panel"><h2>Local storage</h2><p>All game profiles live beside GameVault.</p><p class="path">${root}\\Profiles</p><p class="muted">Copying the complete app folder carries your profiles and backups. Game paths may need updating on another device.</p></section><section class="panel"><h2>Backup defaults</h2><div class="settings-row"><div><strong>Unpinned backups to keep</strong><p>Applied to newly added games. Pinned backups are always additional.</p></div><label><span class="sr-only">Default backup count</span><input id="default-retention" type="number" min="1" max="1000" value="${state.retention}" ${state.keepAll?'disabled':''}></label></div><div class="settings-row"><div><strong>Keep every backup</strong><p>New games will have no automatic retention limit.</p></div><label class="check"><input id="keep-all" type="checkbox" ${state.keepAll?'checked':''}><span>Enabled</span></label></div></section><section class="panel"><h2>Appearance and comfort</h2><div class="settings-row"><div><strong>Color theme</strong><p>Midnight keeps the original blue palette. Crimson adds red and near-black colors.</p></div><label><span class="sr-only">Color theme</span><select id="appearance-theme"><option value="midnight" ${state.theme==='midnight'?'selected':''}>Midnight · default</option><option value="crimson" ${state.theme==='crimson'?'selected':''}>Crimson · red and black</option></select></label></div><div class="settings-row"><div><strong>Reduce motion</strong><p>Also respects your device’s reduced-motion preference.</p></div><label class="check"><input id="reduce-motion" type="checkbox" ${state.reduced?'checked':''}><span>Enabled</span></label></div></section>`; }
function openDialog(body, context) { opener=document.activeElement; formContext=context; document.getElementById('modal-content').innerHTML=body; modal.showModal(); }
function actions(label) { return `<div class="dialog-actions"><button type="button" data-action="cancel">Cancel</button><button class="primary" type="submit">${label}</button></div>`; }
function pathField(name,label,placeholder,value,required=false) {
  return `<div class="field"><label for="path-${name}">${label}</label><div class="path-row"><input id="path-${name}" name="${name}" ${required?'required':''} placeholder="${escapeHtml(placeholder)}" value="${escapeHtml(value)}"><button type="button" data-browse="${name}" aria-label="Browse ${label.toLowerCase()}">Browse…</button></div></div>`;
}
function editDialog(editing=false) {
  const g=editing?currentGame():null;
  openDialog(`<h2 id="modal-title">${g?'Edit game':'Add a game'}</h2><p>Use example paths in this prototype. Nothing on your device is read or changed.</p><label class="field">Game name<input name="name" required maxlength="80" value="${escapeHtml(g?.name||'')}" autofocus></label>${pathField('exe','Game executable','Choose the game executable',g?.exe||'',true)}${pathField('working','Working directory','Defaults to the executable folder',g?.working||'')}${pathField('save','Save location (optional)','Choose this later if saves do not exist yet',g?.save||'')}<p class="path-help">Browse opens a simulated picker here. The Windows app will use native file and folder dialogs; typed paths will also remain available.</p>${g?`<label class="field">Unpinned backup limit<input name="retention" type="number" min="1" max="1000" value="${g.retention}"></label><label class="check"><input name="all" type="checkbox" ${g.keepAll?'checked':''}> Keep every backup for this game</label>`:''}<p id="form-error" class="form-error" role="alert"></p>${actions(g?'Save changes':'Add game')}`,{mode:'edit',id:g?.id});
}
function backup(game) {
  if(busy(game)||!game.save||state.preview==='nosaves') return notify('Stop the game and configure a save location first.');
  state.preview='normal'; document.getElementById('preview-state').value='normal'; game.backing=true; render();
  setTimeout(()=>{game.backing=false;game.backups.unshift({id:crypto.randomUUID(),date:'Just now · demo backup',size:'14.2 MB',pinned:false});const kept=game.backups.filter(b=>!b.pinned&&!b.recovery);if(!game.keepAll){const remove=new Set(kept.slice(game.retention).map(b=>b.id));game.backups=game.backups.filter(b=>!remove.has(b.id));}render();notify('Demo backup completed. No actual files were copied.');},650);
}
document.addEventListener('click',event=>{
  const button=event.target.closest('button,a[data-nav]');if(!button||button.disabled)return;
  if(button.dataset.nav){event.preventDefault();navigate(button.dataset.nav);return;}
  if(button.dataset.game){state.selected=button.dataset.game;state.tab='overview';navigate('details');return;}
  if(button.dataset.filter){state.filter=button.dataset.filter;render();return;}
  if(button.dataset.tab){state.tab=button.dataset.tab;render();return;}
  if(button.dataset.period){state.period=button.dataset.period;render();return;}
  const g=currentGame();
  if(button.dataset.pin){const b=g.backups.find(b=>b.id===button.dataset.pin);b.pinned=!b.pinned;render();return;}
  if(button.dataset.restore){
    if(busy(g))return notify('Stop the game before restoring a backup.');
    if(!g.save||state.preview==='nosaves')return notify('Set a save destination in Edit game before restoring.');
    const b=g.backups.find(b=>b.id===button.dataset.restore);
    openDialog(`<h2 id="modal-title">Restore this backup?</h2><p>${escapeHtml(g.name)} · ${escapeHtml(b.date)}</p><div class="callout"><div><strong>Your current saves will be preserved first</strong><p>If that copy fails, restoration stops. You can return to the preserved saves afterward.</p></div></div><p>Destination</p><p class="path">${escapeHtml(g.save)}</p><p>This prototype only simulates restoration.</p>${actions('Restore backup')}`,{mode:'restore',id:b.id,game:g.id});return;
  }
  switch(button.dataset.action){
    case 'add':editDialog();break;
    case 'edit':editDialog(true);break;
    case 'cancel':modal.close();break;
    case 'show-backups':state.tab='backups';render();break;
    case 'backup':case 'retry':backup(g);break;
    case 'play':if(busy(g)||state.preview==='missing')break;if(games.some(game=>game.running)){notify('End the current demo session before starting another.');break;}g.running=true;sessionStarted=Date.now();if(g.status==='Not started')g.status='Playing';g.last='Today';ticker=setInterval(()=>{if(state.view==='details'&&currentGame().id===g.id)document.querySelectorAll('[data-elapsed]').forEach(el=>el.textContent=elapsed());},1000);render();notify('Demo session started. No actual game was launched.');break;
    case 'end':g.running=false;g.minutes+=Math.max(1,Math.ceil((Date.now()-sessionStarted)/60000));g.sessions.unshift(['Just now · demo session',elapsed()]);clearInterval(ticker);render();if(g.save)backup(g);else notify('Demo session recorded. Set a save location to enable backups.');break;
    case 'correct':openDialog(`<h2 id="modal-title">Correct total playtime</h2><p>Enter a corrected total for ${escapeHtml(g.name)}. A labeled adjustment will appear in the history.</p><label class="field">Total minutes<input name="minutes" type="number" min="0" max="10000000" required value="${g.minutes}" autofocus></label>${actions('Save correction')}`,{mode:'correct',game:g.id});break;
  }
});
document.getElementById('search').addEventListener('input',event=>{state.query=event.target.value;render();});
document.getElementById('preview-state').addEventListener('change',event=>{state.preview=event.target.value;render();});
content.addEventListener('change',event=>{
  if(event.target.id==='game-status'){currentGame().status=event.target.value;notify('Game status updated in the preview.');}
  if(event.target.id==='default-retention'){if(event.target.checkValidity()){state.retention=Number(event.target.value);notify('Default updated for new games.');}else event.target.reportValidity();}
  if(event.target.id==='keep-all'){state.keepAll=event.target.checked;render();}
  if(event.target.id==='appearance-theme'){state.theme=event.target.value;document.documentElement.dataset.theme=state.theme;notify('Theme updated in the preview.');}
  if(event.target.id==='reduce-motion'){state.reduced=event.target.checked;document.body.classList.toggle('reduce-motion',state.reduced);}
});
content.addEventListener('keydown',event=>{if(event.target.matches('[role=tab]')&&['ArrowLeft','ArrowRight','Home','End'].includes(event.key)){event.preventDefault();state.tab=event.key==='Home'?'overview':event.key==='End'?'backups':state.tab==='overview'?'backups':'overview';render();document.getElementById(`tab-${state.tab}`).focus();}});
form.addEventListener('submit',event=>{
  event.preventDefault();const data=new FormData(form);
  if(formContext.mode==='edit'){
    const name=String(data.get('name')).trim(),exe=String(data.get('exe')).trim();
    if(!name||!exe){document.getElementById('form-error').textContent='Enter a game name and executable path.';return;}
    let g=games.find(g=>g.id===formContext.id);
    if(!g){g={id:crypto.randomUUID(),art:'signal',mark:'◇',tag:'Your next adventure',status:'Not started',minutes:0,last:'Never',sessions:[],backups:[],retention:state.retention,keepAll:state.keepAll};games.push(g);}
    g.name=name;g.exe=exe;g.working=String(data.get('working')).trim()||exe.substring(0,Math.max(exe.lastIndexOf('\\'),exe.lastIndexOf('/')));g.save=String(data.get('save')).trim();
    if(formContext.id){g.retention=Number(data.get('retention'));g.keepAll=data.get('all')==='on';}
    state.selected=g.id;state.view='details';state.tab='overview';if(state.preview==='empty'||state.preview==='missing'||state.preview==='nosaves'){state.preview='normal';document.getElementById('preview-state').value='normal';}modal.close();render();notify('Game profile updated in the preview.');
  }else if(formContext.mode==='restore'){
    const g=games.find(g=>g.id===formContext.game);if(busy(g))return notify('Stop the game before restoring.');if(!g.save||state.preview==='nosaves')return notify('Set a save destination in Edit game before restoring.');
    g.backups.unshift({id:crypto.randomUUID(),date:'Just now · preserved current saves',size:'14.2 MB',pinned:false,recovery:true});modal.close();render();notify('Demo restore completed. The previous saves are represented by a recovery snapshot.');
  }else if(formContext.mode==='correct'){
    const g=games.find(g=>g.id===formContext.game),total=Number(data.get('minutes'));g.sessions.unshift(['Manual adjustment',`${total-g.minutes>=0?'+':''}${total-g.minutes}m`]);g.minutes=total;modal.close();render();notify('Demo playtime correction saved.');
  }
});
modal.addEventListener('close',()=>{if(opener?.isConnected)opener.focus();else document.getElementById('content').focus();});
render();
