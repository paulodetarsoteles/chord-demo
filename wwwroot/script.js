(() => {
  const selectors = {
    form: '#uploadForm',
    audio: '#audio',
    player: '#player',
    chord: '#chord',
    submitBtn: '#submitBtn',
    listContainer: '#listContainer'
  };

  const el = {
    form: document.querySelector(selectors.form),
    audio: document.querySelector(selectors.audio),
    player: document.querySelector(selectors.player),
    chord: document.querySelector(selectors.chord),
    submitBtn: document.querySelector(selectors.submitBtn),
    listContainer: document.querySelector(selectors.listContainer),
    themeToggle: document.getElementById('themeToggle')
  };

  // dynamic element populated on init
  el.currentFile = null;

  const state = {
    timeline: [],
    currentIndex: 0
  };

  let storedItems = [];

  function setProcessing(on) {
    if (!el.submitBtn) return;

    if (on) {
      el.submitBtn.disabled = true;
      el.submitBtn.dataset.origText = el.submitBtn.textContent;
      el.submitBtn.textContent = 'Processando...';
      el.submitBtn.setAttribute('aria-busy', 'true');
    } else {
      el.submitBtn.disabled = false;
      el.submitBtn.textContent = el.submitBtn.dataset.origText || 'Enviar e Analisar';
      el.submitBtn.removeAttribute('aria-busy');
    }
  }

  async function uploadFile(formEl) {
    const fd = new FormData(formEl);
    const res = await fetch('/upload', { method: 'POST', body: fd });

    if (!res.ok) throw new Error(await res.text());

    return res.json();
  }

  function showPlayer() {
    if (el.player) el.player.style.display = 'block';
  }

  function setAudioSrcById(id) {
    if (!el.audio) return;

    el.audio.src = `/audio/${id}`;
    showPlayer();
  }

  async function fetchTimelineById(id) {
    const res = await fetch(`/timeline/${id}`);

    if (!res.ok) throw new Error(await res.text());

    const json = await res.json();

    return json.timeline || [];
  }

  function applyTimeline(timeline) {
    state.timeline = timeline || [];
    state.currentIndex = 0;
  }

  async function handleSubmit(e) {
    e.preventDefault();
    setProcessing(true);

    try {
      const data = await uploadFile(el.form);
      const id = data.id;

      setAudioSrcById(id);

      try {
        const tl = await fetchTimelineById(id);
        applyTimeline(tl);
      } catch (err) {
        // timeline may not be ready yet; keep playing
        console.warn('Timeline fetch failed:', err.message);
      }

      el.audio.play().catch(() => {});
      // refresh storage dropdown/list so newly uploaded songs appear immediately
      try {
        await loadStorageList();

        if (el.currentFile) {
          const found = storedItems.find(x => x.id === id);
          el.currentFile.textContent = found ? found.file : id;
        }
      } catch (e) {
        console.warn('Failed to refresh storage list:', e.message);
      }

      el.audio.play().catch(() => {});
    } catch (err) {
      alert('Erro: ' + err.message);
    } finally {
      setProcessing(false);
    }
  }

  function renderEmptyList() {
    if (!el.listContainer) return;
    el.listContainer.innerHTML = '<div class="alert alert-secondary">Nenhuma música disponível.</div>';
  }

  function createListItem(item) {
    const li = document.createElement('li');
    li.className = 'list-group-item d-flex justify-content-between align-items-center';

    const left = document.createElement('div');
    left.textContent = item.file;

    const right = document.createElement('div');
    right.className = 'btn-group btn-group-sm';

    const playBtn = document.createElement('button');
    playBtn.className = 'btn btn-sm btn-outline-primary';
    playBtn.textContent = 'Tocar';
    playBtn.addEventListener('click', async () => playItem(item));

    const dl = document.createElement('a');
    dl.className = 'btn btn-sm btn-outline-secondary';
    dl.href = item.audioUrl;
    dl.textContent = 'Download';
    dl.setAttribute('download', item.file);

    right.appendChild(playBtn);
    right.appendChild(dl);

    li.appendChild(left);
    li.appendChild(right);

    return li;
  }

  async function playItem(item) {
    setAudioSrcById(item.id || item.audioUrl.split('/').pop().split('.')[0]);
    if (el.currentFile) el.currentFile.textContent = item.file || '';
    try {
      const tl = await fetch(item.timelineUrl);
      if (tl.ok) {
        const json = await tl.json();
        applyTimeline(json.timeline || []);
      }
    } catch (e) {
      // ignore timeline fetch errors
    }
    el.audio.play().catch(() => {});
  }

  async function loadStorageList() {
    if (!el.listContainer) return;

    try {
      const res = await fetch('/storage/list');

      if (!res.ok) { 
        renderEmptyList(); 
        return; 
      }

      const items = await res.json();

      if (!items || items.length === 0) { 
        renderEmptyList(); 
        return; 
      }

      // keep items for select handling
      storedItems = items;

      // populate select if exists
      const sel = document.getElementById('storageSelect');

      if (sel) {
        sel.innerHTML = '';
        const empty = document.createElement('option');
        empty.value = '';
        empty.textContent = '-- nenhuma selecionada --';
        sel.appendChild(empty);

        items.forEach(it => {
          const opt = document.createElement('option');
          opt.value = it.id;
          opt.textContent = it.file;
          sel.appendChild(opt);
        });

        sel.addEventListener('change', (e) => {
          const id = e.target.value;

          if (!id) return;

          const item = storedItems.find(x => x.id === id);

          if (item) playItem(item);
        });
      }

      el.listContainer.innerHTML = '';
    } catch (err) {
      el.listContainer.textContent = 'Erro ao carregar lista de músicas.';
    }
  }

  function updateChordDisplay() {
    if (!el.audio || !el.chord) return;

    const t = el.audio.currentTime;

    if (!state.timeline || state.timeline.length === 0) { 
      el.chord.textContent = '—'; 
      return; 
    }

    while (state.currentIndex < state.timeline.length && t > state.timeline[state.currentIndex].end) state.currentIndex++;
    
    while (state.currentIndex > 0 && t < state.timeline[state.currentIndex].start) state.currentIndex--;

    if (state.currentIndex >= state.timeline.length) { 
      el.chord.textContent = '—'; 
      return; 
    }

    const seg = state.timeline[state.currentIndex];

    if (t >= seg.start && t <= seg.end) el.chord.textContent = `${seg.label} (${(seg.confidence||0).toFixed(2)})`;
    else el.chord.textContent = '—';
  }

  function init() {
    if (el.form) el.form.addEventListener('submit', handleSubmit);

    if (el.audio) el.audio.addEventListener('timeupdate', updateChordDisplay);

    el.currentFile = document.getElementById('currentFile');

    loadStorageList();
    initTheme();

    if (el.themeToggle) el.themeToggle.addEventListener('click', toggleTheme);
  }

  // public init
  init();
  
  // Theme handling
  function applyTheme(isDark){
    if (isDark) document.documentElement.classList.add('dark-theme');
    else document.documentElement.classList.remove('dark-theme');
  }

  function initTheme(){
    try{
      const pref = localStorage.getItem('theme');
      const isDark = pref === 'dark' || (!pref && window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
      applyTheme(isDark);
      updateThemeButtonLabel(isDark);
    } catch(e){}
  }

  function updateThemeButtonLabel(isDark){
    if (!el.themeToggle) return;
    
    el.themeToggle.textContent = isDark ? 'Tema: Escuro' : 'Tema: Claro';
  }

  function toggleTheme(){
    try{
      const isDark = document.documentElement.classList.toggle('dark-theme');
      localStorage.setItem('theme', isDark ? 'dark' : 'light');
      updateThemeButtonLabel(isDark);
    } catch(e){}
  }
})();
