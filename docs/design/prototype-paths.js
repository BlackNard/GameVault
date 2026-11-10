'use strict';
// Simulated browser dialog only. Production WPF uses native Windows pickers.
(() => {
  const picker=document.getElementById('path-picker');
  const pickerForm=document.getElementById('path-picker-form');
  let fieldName=null, browseButton=null;
  const parentFolder=path=>path.substring(0,Math.max(path.lastIndexOf('\\'),path.lastIndexOf('/')));
  const examples={
    exe:[['Resident Evil 2','D:\\Games\\Resident Evil 2\\re2.exe'],['Aster Vale','D:\\Games\\Aster Vale\\game.exe'],['Signal Lost','D:\\Games\\Signal Lost\\game.exe']],
    working:[['Resident Evil 2','D:\\Games\\Resident Evil 2'],['Aster Vale','D:\\Games\\Aster Vale'],['Custom game folder','E:\\My Games\\Portable Game']],
    save:[['Resident Evil 2 saves','C:\\Users\\Player\\Saved Games\\RE2'],['Aster Vale saves','C:\\Users\\Player\\Saved Games\\AsterVale'],['Custom save folder','E:\\Game Saves\\Portable Game']]
  };
  form.addEventListener('click',event=>{
    const button=event.target.closest('[data-browse]');
    if(!button)return;
    event.preventDefault();
    fieldName=button.dataset.browse;
    browseButton=button;
    const isFile=fieldName==='exe';
    const title=isFile?'Choose game executable':fieldName==='working'?'Choose working directory':'Choose save location';
    const current=form.elements.namedItem(fieldName).value;
    document.getElementById('path-picker-content').innerHTML=`<h2 id="picker-title">${title}</h2><p>Preview dialog · choose an example below. The Windows app will open the real system ${isFile?'file':'folder'} browser.</p><div class="picker-location">${isFile?'This PC › Games › Executable files (*.exe)':'This PC › Folders'}</div><fieldset class="picker-options"><legend class="sr-only">Example ${isFile?'files':'folders'}</legend>${examples[fieldName].map(([name,path],i)=>`<label class="picker-option"><input type="radio" name="path" value="${escapeHtml(path)}" ${path===current||(!examples[fieldName].some(e=>e[1]===current)&&i===0)?'checked':''}><span><strong>${name}</strong><small>${escapeHtml(path)}</small></span></label>`).join('')}</fieldset><div class="dialog-actions"><button type="button" id="picker-cancel">Cancel</button><button type="submit" class="primary">${isFile?'Select executable':'Select folder'}</button></div>`;
    picker.showModal();
  });
  picker.addEventListener('click',event=>{if(event.target.closest('#picker-cancel'))picker.close();});
  pickerForm.addEventListener('submit',event=>{
    event.preventDefault();
    const selected=new FormData(pickerForm).get('path');
    if(typeof selected!=='string')return;
    const input=form.elements.namedItem(fieldName);
    const previous=input.value;
    input.value=selected;
    if(fieldName==='exe'){
      const working=form.elements.namedItem('working');
      if(!working.value.trim()||working.value===parentFolder(previous))working.value=parentFolder(selected);
    }
    input.dispatchEvent(new Event('input',{bubbles:true}));
    picker.close();
  });
  picker.addEventListener('close',()=>{if(browseButton?.isConnected)browseButton.focus();});
})();
