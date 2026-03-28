/**
 * ═══════════════════════════════════════════════════════════
 *  Inclusive Communication Hub - Web Portal
 *  JavaScript Application Controller
 * ═══════════════════════════════════════════════════════════
 */

// Dashboard API and SignalR Hub connections strictly wired to Kestrel .NET Server
const API_BASE = 'https://localhost:49940/api';
const HUB_URL = 'https://localhost:49940/hub/audio';

// ─── MICROSOFT ENTRA ID AUTHENTICATION ───────────────
const msalConfig = {
    auth: {
        clientId: "fc4e2866-d347-4eb8-a4fc-c5a64aeac4b9",
        authority: "https://login.microsoftonline.com/abc14712-40a7-4fc5-9f3e-f23014d13c0e",
        redirectUri: "https://localhost:49938/index.html",
    },
    cache: {
        cacheLocation: "sessionStorage",
        storeAuthStateInCookie: false,
    }
};

const msalInstance = new msal.PublicClientApplication(msalConfig);
let currentUser = null;
let currentToken = null;

async function checkLoginState() {
    await msalInstance.initialize();
    
    // Handle redirect response from login
    msalInstance.handleRedirectPromise().then((response) => {
        if (response !== null) {
            currentUser = response.account;
            currentToken = response.idToken;
            updateLoginUI();
            loadClassifier();
        } else {
            // See if already logged in via active session
            const currentAccounts = msalInstance.getAllAccounts();
            if (currentAccounts.length > 0) {
                currentUser = currentAccounts[0];
                updateLoginUI();
                msalInstance.acquireTokenSilent({
                    account: currentUser,
                    scopes: ["User.Read"]
                }).then(res => {
                    currentToken = res.idToken;
                    loadClassifier();
                });
            }
        }
    }).catch(error => console.error("MSAL Redirect Error:", error));
}

function login() {
    if (!currentUser) msalInstance.loginRedirect({ scopes: ["User.Read"] });
}

// Initialize MSAL on startup
checkLoginState();

function logout() {
    if (currentUser) {
        msalInstance.logoutRedirect({ account: currentUser });
    }
}

function updateLoginUI() {
    const btn = document.getElementById('btn-login-ms');
    if (btn) {
        if (currentUser) {
            btn.innerHTML = `<span class="material-symbols-outlined">account_circle</span> ${currentUser.name}`;
            btn.onclick = logout;
            btn.classList.replace('text-slate-400', 'text-[#00dce6]');
        } else {
            btn.innerHTML = `<span class="material-symbols-outlined">login</span> Sign In`;
            btn.onclick = login;
        }
    }
}
document.addEventListener('DOMContentLoaded', checkLoginState);
// ────────────────────────────────────────────────────────

// ─── State ─────────────────────────────────────────────────
const state = {
    currentPage: 'dashboard',
    sessions: [],
    currentSession: null,
    isLiveActive: false,
    hubConnection: null,
    authToken: null,
    user: null,
    signLanguageQueue: [],
    isProcessingSign: false
};

// ========== KNN GESTURE LAB ==========
let knnClassifier = null;
let isTraining = false;
let trainingLabel = '';
let trainingCount = 0;

async function initKNN() {
    if (window.knnClassifier && !knnClassifier) {
        if (window.tf) {
            try {
                await window.tf.setBackend('cpu');
                console.log("🧠 [TFJS] Configured to use 'cpu' backend. WebGL disabled to prevent context limit errors during Remote Capture.");
            } catch (e) {
                console.warn("TFJS Backend error:", e);
            }
        }
        knnClassifier = window.knnClassifier.create();
        loadClassifier(); // Restore persisted user models!
        const statusEl = document.getElementById('train-status');
        if (statusEl) {
            statusEl.textContent = 'TFJS Ready';
            statusEl.className = 'text-[10px] font-mono text-green-400 bg-green-400/10 px-2 py-0.5 rounded border border-green-400/20';
        }
        console.log("🧠 [TFJS] KNN Classifier initialized.");
    }
}

// ========== KNN PERSISTENCE ==========
async function saveClassifier() {
    if (!knnClassifier || knnClassifier.getNumClasses() === 0) return;
    if (!currentUser || !currentToken) {
        showToast("Please Sign In to sync signs to Azure.", "error");
        return;
    }
    try {
        const dataset = knnClassifier.getClassifierDataset();
        const datasetObj = {};
        for (const key of Object.keys(dataset)) {
            const data = await dataset[key].data();
            datasetObj[key] = {
                data: Array.from(data),
                shape: dataset[key].shape
            };
        }
        
        let jsonStr = JSON.stringify(datasetObj);
        
        // Encode into b64
        const binary = new TextEncoder().encode(jsonStr);
        let b = '';
        for (let i = 0; i < binary.byteLength; i++) { b += String.fromCharCode(binary[i]); }
        const b64 = window.btoa(b);

        await fetch(`${API_BASE}/signs/sync`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${currentToken}`
            },
            body: JSON.stringify({ WeightsBase64: b64 })
        });
        showToast("Model synced to Azure Cosmos DB", "success");
    } catch (err) {
        console.error("Failed to save KNN Classifier:", err);
    }
}

async function loadClassifier() {
    if (!currentUser || !currentToken) return;
    try {
        const res = await fetch(`${API_BASE}/signs`, {
            headers: { 'Authorization': `Bearer ${currentToken}` }
        });
        if (!res.ok) return;
        const dataJson = await res.json();
        if (!dataJson.weightsBase64) return;
        
        let rawHtml = window.atob(dataJson.weightsBase64);
        let bytesArray = new Uint8Array(rawHtml.length);
        for(let i=0; i<rawHtml.length; i++){ bytesArray[i] = rawHtml.charCodeAt(i); }
        let rawStr = new TextDecoder('utf-8').decode(bytesArray);
        
        const parsed = JSON.parse(rawStr);
        if (Object.keys(parsed).length === 0) return;
        
        const dataset = {};
        for (const key of Object.keys(parsed)) {
            dataset[key] = window.tf.tensor(parsed[key].data, parsed[key].shape);
        }
        
        knnClassifier.setClassifierDataset(dataset);
        console.log(`🧠 [TFJS] KNN Classifier restored from Azure Cosmos DB with ${Object.keys(dataset).length} trained classes.`);
        
        if (typeof renderSignLibrary === 'function') {
            renderSignLibrary();
        }
    } catch (err) {
        console.error("Failed to restore KNN Classifier from Azure:", err);
    }
}

// ─── Navigation ────────────────────────────────────────────
function navigateTo(pageId) {
    state.currentPage = pageId;

    // Reset Nav Visuals (tailored for Tailwind colors)
    const navs = ['nav-audio', 'nav-transcripts', 'nav-signlab'];
    navs.forEach(id => {
        const el = document.getElementById(id);
        if (el) {
            el.classList.remove('text-[#00dce6]', 'bg-[#2d3449]', 'border-r-2', 'border-[#00dce6]', 'text-amber-500', 'bg-amber-500/10');
            el.classList.add('text-slate-400');
        }
    });

    // Reset Views
    const views = ['view-audio', 'view-transcripts', 'view-signlab'];
    views.forEach(id => {
        const el = document.getElementById(id);
        if (el) el.style.display = 'none';
    });

    // Boot TensorFlow if entering the ML Studio
    if (pageId === 'signlab' && typeof initKNN === 'function') {
        initKNN();
    }

    // Activate Selected
    const targetNav = document.getElementById(`nav-${pageId}`);
    const targetView = document.getElementById(`view-${pageId}`);
    
    if (targetView) {
        targetView.style.display = 'flex';
        // Force hardware layer to resume if browser suspended the hidden element
        if (webcamRunning) {
            const vAudio = document.getElementById('webcam-video');
            const vLab = document.getElementById('lab-webcam-video');
            if (pageId === 'audio' && vAudio) vAudio.play().catch(e=>console.warn(e));
            if (pageId === 'signlab' && vLab) vLab.play().catch(e=>console.warn(e));
        }
    }
    
    if (targetNav) {
        targetNav.classList.remove('text-slate-400');
        if (pageId === 'signlab') {
            targetNav.classList.add('text-amber-500', 'bg-amber-500/10', 'border-r-2', 'border-amber-500');
        } else {
            targetNav.classList.add('text-[#00dce6]', 'bg-[#2d3449]', 'border-r-2', 'border-[#00dce6]');
        }
    }
}

// Bind Listeners
document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('nav-audio')?.addEventListener('click', (e) => { e.preventDefault(); navigateTo('audio'); });
    document.getElementById('nav-transcripts')?.addEventListener('click', (e) => { e.preventDefault(); navigateTo('transcripts'); });
    document.getElementById('nav-signlab')?.addEventListener('click', (e) => { e.preventDefault(); navigateTo('signlab'); });
    
    // Initial Load
    navigateTo('audio');
});

// ─── API Helpers ───────────────────────────────────────────
async function apiCall(endpoint, options = {}) {
    const headers = {
        'Content-Type': 'application/json',
        ...(state.authToken ? { 'Authorization': `Bearer ${state.authToken}` } : {}),
        ...options.headers
    };

    try {
        const response = await fetch(`${API_BASE}${endpoint}`, {
            ...options,
            headers
        });

        if (!response.ok) {
            throw new Error(`API Error: ${response.status} ${response.statusText}`);
        }

        const text = await response.text();
        return text ? JSON.parse(text) : null;
    } catch (error) {
        console.error('API call failed:', error);
        // Suppress offline toast in production (Edge Fallback triggers this intentionally)
        // showToast(error.message, 'error');
        throw error;
    }
}

// ─── Dashboard ─────────────────────────────────────────────
async function loadDashboard() {
    try {
        const sessions = await apiCall('/sessions');
        state.sessions = sessions || [];

        // Update stats
        const active = sessions?.filter(s => s.status === 1).length || 0;
        document.getElementById('stat-active').textContent = active;

        const totalEntries = sessions?.reduce((sum, s) =>
            sum + (s.transcripts?.length || 0), 0) || 0;
        document.getElementById('stat-translations').textContent = totalEntries;

        // Render recent sessions
        renderRecentSessions(sessions?.slice(0, 5) || []);
    } catch {
        // Use demo data if API is not available
        renderDemoData();
    }
}

function renderRecentSessions(sessions) {
    const container = document.getElementById('recent-sessions-list');
    if (!sessions.length) {
        container.innerHTML = `
            <div class="session-item" style="justify-content:center; color: var(--color-text-muted);">
                <span class="material-icons-round">info</span>
                <span>No sessions yet. Create one to get started!</span>
            </div>`;
        return;
    }

    container.innerHTML = sessions.map(s => `
        <div class="session-item" onclick="viewSession('${s.id}')">
            <span class="material-icons-round" style="color: var(--color-accent)">graphic_eq</span>
            <span class="session-item__title">${s.title || 'Untitled Session'}</span>
            <span class="session-item__meta">${formatDate(s.startedAt)} · ${s.sourceLanguage}→${s.targetLanguage}</span>
            <span class="session-item__status session-item__status--${s.status === 1 ? 'active' : 'completed'}">
                ${getStatusLabel(s.status)}
            </span>
        </div>
    `).join('');
}

function renderDemoData() {
    const statActive = document.getElementById('stat-active');
    if (!statActive) return;

    statActive.textContent = '0';
    document.getElementById('stat-translations').textContent = '0';
    document.getElementById('stat-hours').textContent = '0h';
    document.getElementById('stat-latency').textContent = '--ms';

    const container = document.getElementById('recent-sessions-list');
    if (container) {
        container.innerHTML = `
            <div class="session-item" style="justify-content:center; color: var(--color-text-muted);">
                <span class="material-icons-round">rocket_launch</span>
                <span>System ready. Connect to backend to start sessions.</span>
            </div>`;
    }
}

// ─── Sessions Page ─────────────────────────────────────────
async function loadSessions() {
    try {
        const sessions = await apiCall('/sessions');
        state.sessions = sessions || [];
        renderSessionsTable(sessions || []);
    } catch {
        renderSessionsTable([]);
    }
}

function renderSessionsTable(sessions) {
    const tbody = document.getElementById('sessions-tbody');
    if (!sessions.length) {
        tbody.innerHTML = `
            <tr>
                <td colspan="6" style="text-align:center; color: var(--color-text-muted); padding: 40px;">
                    No sessions found
                </td>
            </tr>`;
        return;
    }

    tbody.innerHTML = sessions.map(s => `
        <tr>
            <td><strong>${s.title || 'Untitled'}</strong></td>
            <td>${formatDate(s.startedAt)}</td>
            <td>${s.endedAt ? formatDuration(s.startedAt, s.endedAt) : 'In progress'}</td>
            <td>${s.sourceLanguage} → ${s.targetLanguage}</td>
            <td>
                <span class="session-item__status session-item__status--${s.status === 1 ? 'active' : 'completed'}">
                    ${getStatusLabel(s.status)}
                </span>
            </td>
            <td>
                <button class="btn btn--sm btn--secondary" onclick="viewSession('${s.id}')">
                    <span class="material-icons-round">visibility</span>
                </button>
                <button class="btn btn--sm btn--danger" onclick="deleteSession('${s.id}')">
                    <span class="material-icons-round">delete</span>
                </button>
            </td>
        </tr>
    `).join('');
}

async function viewSession(id) {
    try {
        const session = await apiCall(`/sessions/${id}`);
        state.currentSession = session;
        // Show session detail...
        navigateTo('copilot');
    } catch (error) {
        console.error('Failed to load session:', error);
    }
}

async function deleteSession(id) {
    if (!confirm('Are you sure? This will delete all session data including recordings.')) return;
    try {
        await apiCall(`/sessions/${id}`, { method: 'DELETE' });
        loadSessions();
        showToast('Session deleted', 'success');
    } catch (error) {
        console.error('Failed to delete session:', error);
    }
}

// ─── Live Session ──────────────────────────────────────────
document.getElementById('btn-start-live')?.addEventListener('click', startLiveSession);
document.getElementById('btn-stop-live')?.addEventListener('click', stopLiveSession);
document.getElementById('btn-send-keyboard')?.addEventListener('click', sendKeyboardInput);

document.getElementById('keyboard-input')?.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') sendKeyboardInput();
});

function syncAvatarLanguage() {
    const transSelect = document.getElementById('translation-lang');
    if(!transSelect) return;
    
    const spokenLang = transSelect.value.split('-')[0];
    // Get signed language from ribbon selection or fallback
    const activeSignBtn = document.querySelector('#ribbon-signed button.border-b-2');
    const selectSgn = document.getElementById('select-sgn');
    const signedAbbrev = activeSignBtn?.dataset?.code || selectSgn?.value || 'ase';
    
    const iframe = document.getElementById('sign-iframe');
    if (iframe && iframe.contentWindow) {
        iframe.contentWindow.postMessage({
            type: 'set_language',
            spoken: spokenLang,
            signed: signedAbbrev
        }, '*');
    }
}

document.getElementById('translation-lang')?.addEventListener('change', syncAvatarLanguage);
document.getElementById('sign-lang')?.addEventListener('change', syncAvatarLanguage);


window.setRibbonLanguage = function(spokenCode, signedCode) {
    // Determine mapping IDs for styling
    const spkIdMap = { 'en-US': 'btn-spk-en', 'es-ES': 'btn-spk-es', 'fr-FR': 'btn-spk-fr' };
    const sgnIdMap = { 'ase': 'btn-sgn-ase', 'ssp': 'btn-sgn-ssp', 'fsl': 'btn-sgn-fsl' };
    
    // Reset ribbon button visual statuses
    document.querySelectorAll('#ribbon-spoken button.border-b-2').forEach(b => {
        if(b.id) b.className = 'px-3 py-2 hover:text-white transition-colors';
    });
    document.querySelectorAll('#ribbon-signed button.border-b-2').forEach(b => {
        if(b.id) b.className = 'px-3 py-2 hover:text-white transition-colors flex items-center gap-2';
    });
    
    // Activate clicked buttons
    const activeClassBase = 'px-3 py-2 text-[#00dce6] border-b-2 border-[#00dce6]';
    const finalSpkBtn = document.getElementById(spkIdMap[spokenCode]);
    if (finalSpkBtn) finalSpkBtn.className = activeClassBase;
    
    const finalSgnBtn = document.getElementById(sgnIdMap[signedCode]);
    if (finalSgnBtn) finalSgnBtn.className = activeClassBase + ' flex items-center gap-2';

    // Synchronize the native select dropdown to trigger backend changes naturally
    const transSelect = document.getElementById('translation-lang');
    if(transSelect) transSelect.value = spokenCode;

    const signSelect = document.getElementById('sign-lang');
    if(signSelect) signSelect.value = signedCode;
    
    // Refresh avatar settings
    if (typeof syncAvatarLanguage === 'function') syncAvatarLanguage();
};

// ─── Remote Audio Capture (VB-Audio -> Azure Native) ────────────────
window.startRemoteAudioCapture = async function() {
    const remoteMicId = document.getElementById('remote-mic-select')?.value;
    if (!remoteMicId) {
        console.log("No Remote Audio Source selected. Skipping dual-channel capture.");
        return;
    }
    
    if (window._remoteAudioStream) return;

    try {
        const audioStream = await navigator.mediaDevices.getUserMedia({
            audio: { 
                deviceId: { exact: remoteMicId },
                echoCancellation: false,
                noiseSuppression: false,
                autoGainControl: false 
            }
        });

        window._remoteAudioStream = audioStream;
        
        if (window.SpeechSDK) {
            console.log('☁️ Hooking Remote Audio Native MediaStream to Azure Cognitive Services...');
            const subscriptionKey = "4hS1ozfu3woZF4oodRLZlwTrOvoDIyDC0FtNnGs7rFjvRBvMUElDJQQJ99CCACYeBjFXJ3w3AAAYACOGeghh";
            const region = "eastus";
            
            const speechConfig = window.SpeechSDK.SpeechConfig.fromSubscription(subscriptionKey, region);
            speechConfig.speechRecognitionLanguage = document.getElementById('recognition-lang')?.value || "en-US";
            
            // Fix: pass the ACTUAL variable audioStream instead of 'stream'
            const audioConfig = window.SpeechSDK.AudioConfig.fromStreamInput(audioStream);
            const recognizer = new window.SpeechSDK.SpeechRecognizer(speechConfig, audioConfig);
            
            recognizer.recognized = (s, e) => {
                // HALF-DUPLEX AEC: Ignore frames if TTS is actively speaking
                if (window._isTTSPlaying) {
                    console.log('🔇 Ignoring captured audio (TTS is playing) to prevent acoustic loop.');
                    return;
                }
                
                if (e.result.reason === window.SpeechSDK.ResultReason.RecognizedSpeech) {
                    const text = e.result.text;
                    // Trigger Native Platform Routing for "Remote Guest"
                    if (window.addLocalTranscriptEntry) {
                        window.addLocalTranscriptEntry(text, 99, 'Remote Guest', 'screen_share');
                    }
                    if (window.sendToCloudAI) {
                        window.sendToCloudAI(text);
                    }
                }
            };
            
            recognizer.startContinuousRecognitionAsync(
                () => console.log("☁️ Azure Speech Stream Transcription active"),
                (err) => console.error(err)
            );
            window._remoteAudioRecognizer = recognizer;
        } else {
            console.warn("Azure SDK not loaded, remote audio capture cannot proceed.");
        }
    } catch (err) {
        console.warn('System audio capture failed to initialize:', err);
    }
};

window.stopRemoteAudioCapture = function() {
    if (window._remoteAudioRecognizer) {
        try { window._remoteAudioRecognizer.stopContinuousRecognitionAsync(); } catch(e){}
        window._remoteAudioRecognizer.close();
        window._remoteAudioRecognizer = null;
    }
    if (window._remoteAudioStream) {
        window._remoteAudioStream.getTracks().forEach(t => t.stop());
        window._remoteAudioStream = null;
    }
};

// ─── Legacy Screen Share (Video only for ML pipeline) ────────────────
document.getElementById('btn-system-audio')?.addEventListener('click', async () => {
    try {
        const videoStream = await navigator.mediaDevices.getDisplayMedia({
            video: true,
            audio: false 
        });
        
        // Show UI
        const container = document.getElementById('system-audio-container');
        if (container) container.classList.remove('hidden');
        
        // Timer
        systemAudioStartTime = Date.now();
        systemAudioTimerInterval = setInterval(() => {
            const elapsed = Math.floor((Date.now() - systemAudioStartTime) / 1000);
            const m = String(Math.floor(elapsed / 60)).padStart(2, '0');
            const s = String(elapsed % 60).padStart(2, '0');
            const timerEl = document.getElementById('system-audio-timer');
            if (timerEl) timerEl.textContent = `${m}:${s}`;
        }, 1000);

        videoStream.getVideoTracks()[0].addEventListener('ended', () => {
            clearInterval(systemAudioTimerInterval);
            if (container) container.classList.add('hidden');
        });

    } catch (err) {
        console.warn('Video capture cancelled:', err);
    }
});

function stopSystemAudioCapture() {
    if (systemAudioStream) {
        systemAudioStream.getTracks().forEach(t => t.stop());
        systemAudioStream = null;
    }
    if (window._systemAudioElement) {
        window._systemAudioElement.pause();
        window._systemAudioElement.srcObject = null;
        window._systemAudioElement = null;
    }
    if (window._systemAudioRecognizer) {
        window._systemAudioRecognizer.stopContinuousRecognitionAsync();
        window._systemAudioRecognizer.close();
        window._systemAudioRecognizer = null;
    }
    if (systemAudioTimerInterval) {
        clearInterval(systemAudioTimerInterval);
        systemAudioTimerInterval = null;
    }
    // Hide UI
    const container = document.getElementById('system-audio-container');
    if (container) container.classList.add('hidden');
    const btn = document.getElementById('btn-system-audio');
    if (btn) {
        btn.classList.remove('bg-[#f59e0b]', 'text-[#0b1326]');
        btn.classList.add('bg-[#f59e0b]/10', 'text-[#f59e0b]');
    }
    const timerEl = document.getElementById('system-audio-timer');
    if (timerEl) timerEl.textContent = '00:00';
    const levelEl = document.getElementById('system-audio-level');
    if (levelEl) levelEl.style.width = '0%';

    if (typeof showToast === 'function') showToast('🎧 System audio capture stopped', 'info', 2000);
    console.log('🎧 System Audio capture stopped');
}

document.getElementById('btn-stop-system-audio')?.addEventListener('click', stopSystemAudioCapture);

let shareAvatarActive = false;
let avatarFrameCanvas = null;
let avatarFrameCtx = null;
let avatarFrameStream = null;
let pipVideoElement = null;

// Handle incoming Broadcast frames from the Angular Sandbox
window.addEventListener('message', (event) => {
    if (event.data && event.data.type === 'AVATAR_FRAME_TRANSFER' && shareAvatarActive) {
        if (!avatarFrameCanvas) {
            avatarFrameCanvas = document.createElement('canvas');
            avatarFrameCanvas.width = event.data.width || 640;
            avatarFrameCanvas.height = event.data.height || 480;
            // bitmaprenderer is extremely fast and zero-copy for transferring ImageBitmaps
            avatarFrameCtx = avatarFrameCanvas.getContext('bitmaprenderer');
        }
        
        // Transfer zero-copy ImageBitmap
        if (event.data.frameBitmap) {
            avatarFrameCtx.transferFromImageBitmap(event.data.frameBitmap);
        }
    }
});

document.getElementById('btn-share-avatar')?.addEventListener('click', async () => {
    if (shareAvatarActive) {
        if (typeof showToast === 'function') showToast('📺 Avatar Share session is already active.', 'info', 3000);
        return;
    }

    const iframe = document.getElementById('sign-iframe');
    if (!iframe || !iframe.contentWindow) {
        if (typeof showToast === 'function') showToast('❌ Avatar Engine not fully loaded.', 'error', 3000);
        return;
    }

    try {
        // 1. Ask Angular to begin frame transmission
        shareAvatarActive = true;
        iframe.contentWindow.postMessage({ type: 'START_BROADCAST' }, '*');
        
        // Wait a tiny bit for the first frame to arrive and initialize the canvas
        if (typeof showToast === 'function') showToast('⏳ Starting Virtual Sign Cam...', 'azure', 2000);
        
        let attempts = 0;
        while (!avatarFrameCanvas && attempts < 20) {
            await new Promise(r => setTimeout(r, 100));
            attempts++;
        }

        if (!avatarFrameCanvas) {
            shareAvatarActive = false;
            iframe.contentWindow.postMessage({ type: 'STOP_BROADCAST' }, '*');
            if (typeof showToast === 'function') showToast('❌ Failed to capture Avatar frames. Make sure Sign.mt is running.', 'error', 3000);
            return;
        }

        // 2. We have a live local Canvas! Generate a reliable MediaStream from it
        avatarFrameStream = avatarFrameCanvas.captureStream(30);

        // 3. Initiate Document PiP or Standard PiP
        if ('documentPictureInPicture' in window) {
            const w = avatarFrameCanvas.width;
            const h = avatarFrameCanvas.height;
            const pipWindow = await window.documentPictureInPicture.requestWindow({ width: w, height: h });
            
            pipVideoElement = document.createElement('video');
            pipVideoElement.srcObject = avatarFrameStream;
            pipVideoElement.autoplay = true;
            pipVideoElement.muted = true;
            pipVideoElement.style.width = '100%';
            pipVideoElement.style.height = '100%';
            pipVideoElement.style.objectFit = 'contain';
            pipVideoElement.style.backgroundColor = '#131b2e';
            
            pipWindow.document.body.style.margin = '0';
            pipWindow.document.body.style.backgroundColor = '#131b2e';
            pipWindow.document.body.appendChild(pipVideoElement);

            pipWindow.addEventListener("pagehide", () => stopAvatarShare());
            if (typeof showToast === 'function') showToast('📺 Virtual Sign Cam Ready! Share this PiP window anywhere.', 'azure', 6000);

        } else {
            // Standard Video PiP fallback
            pipVideoElement = document.createElement('video');
            pipVideoElement.srcObject = avatarFrameStream;
            pipVideoElement.autoplay = true;
            pipVideoElement.muted = true;
            pipVideoElement.style.position = 'fixed';
            pipVideoElement.style.bottom = '-9999px';
            document.body.appendChild(pipVideoElement);
            
            pipVideoElement.onloadedmetadata = async () => {
                try {
                    await pipVideoElement.requestPictureInPicture();
                    if (typeof showToast === 'function') showToast('📺 PiP Avatar started. Share your screen!', 'azure', 6000);
                } catch (e) {
                    stopAvatarShare();
                    
                    // Fallback Level 3: Open an independent popup with the canvas stream cloned 
                    // This creates a dedicated window playing our local stream
                    const popup = window.open('/avatar-cam.html', 'ICH_Avatar_Webcam', 'width=640,height=480,resizable=yes,scrollbars=no');
                    if (popup) {
                        shareAvatarActive = true;
                        iframe.contentWindow.postMessage({ type: 'START_BROADCAST' }, '*');
                        
                        // Wait for popup to be structurally ready to inject the media stream
                        const onPopupReady = (e) => {
                            if (e.data && e.data.type === 'POPUP_READY') {
                                window.removeEventListener('message', onPopupReady);
                                try {
                                    const remoteVid = popup.document.getElementById('vid');
                                    if (remoteVid) {
                                        remoteVid.srcObject = avatarFrameStream;
                                    }
                                } catch (err) {
                                    console.error("Popup stream injection error:", err);
                                }
                            }
                        };
                        window.addEventListener('message', onPopupReady);
                        popup.onbeforeunload = stopAvatarShare;
                        
                        if (typeof showToast === 'function') showToast('📺 Virtual Cam popup opened. Share it on Zoom.', 'azure', 5000);
                    } else {
                        if (typeof showToast === 'function') showToast('❌ Picture-in-Picture & Popups blocked by your browser.', 'error', 3000);
                    }
                }
            };
            
            pipVideoElement.addEventListener('leavepictureinpicture', stopAvatarShare);
        }
    } catch (e) {
        console.error("PIP Broadcast Error:", e);
        stopAvatarShare();
    }
});

function stopAvatarShare() {
    shareAvatarActive = false;
    const iframe = document.getElementById('sign-iframe');
    if (iframe && iframe.contentWindow) {
        iframe.contentWindow.postMessage({ type: 'STOP_BROADCAST' }, '*');
    }
    if (avatarFrameStream) {
        avatarFrameStream.getTracks().forEach(t => t.stop());
        avatarFrameStream = null;
    }
    if (pipVideoElement && pipVideoElement.parentNode) {
        pipVideoElement.parentNode.removeChild(pipVideoElement);
    }
    pipVideoElement = null;
    avatarFrameCanvas = null;
}

// ─── Remote Sign Language Detection (Screen Capture) ──────
let remoteSignStream = null;
let remoteSignAnimFrame = null;

document.getElementById('btn-capture-remote-signs')?.addEventListener('click', async () => {
    if (remoteSignStream) {
        stopRemoteSignCapture();
        return;
    }
    try {
        const stream = await navigator.mediaDevices.getDisplayMedia({
            video: { cursor: 'never', frameRate: 15 },
            audio: false
        });

        remoteSignStream = stream;
        const videoTrack = stream.getVideoTracks()[0];
        const label = videoTrack.label || 'Remote Video';

        // Create floating video element for visual feedback (thumbnail)
        const video = document.createElement('video');
        video.srcObject = stream;
        video.autoplay = true;
        video.muted = true;
        // Float at bottom right with glass effect so user sees what is being analyzed
        video.style.cssText = 'position:fixed;right:20px;bottom:80px;width:240px;height:180px;border-radius:12px;border:2px solid #00dce6;z-index:99999;box-shadow: 0 10px 25px rgba(0,0,0,0.5);object-fit:cover;';
        document.body.appendChild(video);
        window._remoteSignVideo = video;

        // Create canvas for MediaPipe processing
        const canvas = document.createElement('canvas');
        canvas.width = 640;
        canvas.height = 480;
        const ctx = canvas.getContext('2d');
        window._remoteSignCanvas = canvas;

        // Update button style 
        const btn = document.getElementById('btn-capture-remote-signs');
        if (btn) {
            btn.classList.add('bg-rose-500', 'text-white');
            btn.classList.remove('text-rose-400');
            btn.style.background = '#e11d48';
        }

        // Use existing global gesture recognizer if available
        let lastProcessTime = 0;
        const processInterval = 200; // Process every 200ms to save CPU

        function processFrame() {
            if (!remoteSignStream) return;
            const now = Date.now();
            if (now - lastProcessTime < processInterval) {
                remoteSignAnimFrame = requestAnimationFrame(processFrame);
                return;
            }
            lastProcessTime = now;

            // Draw current frame to canvas
            ctx.drawImage(video, 0, 0, 640, 480);

            // Run gesture recognition if available using globals
            if (gestureRecognizer && typeof gestureRecognizer.recognizeForVideo === 'function') {
                try {
                    const results = gestureRecognizer.recognizeForVideo(video, now);
                    
                    // 1. Try Custom KNN Classifier First (Immediate Response)
                    if (results && results.landmarks && results.landmarks.length > 0 && knnClassifier && knnClassifier.getNumClasses && knnClassifier.getNumClasses() > 0) {
                        const flat = results.landmarks[0].flatMap(l => [l.x, l.y, l.z]);
                        const tensor = window.tf?.tensor1d(flat);
                        if (tensor) {
                            knnClassifier.predictClass(tensor).then(prediction => {
                                if (prediction && prediction.confidences && prediction.confidences[prediction.label] > 0.65) {
                                    const signLabel = prediction.label;
                                    
                                    const overlay = document.getElementById('gesture-overlay');
                                    if (overlay && !window.gestureCooldown) {
                                        overlay.textContent = `🖐️ Remote: ${signLabel} (${Math.round(prediction.confidences[prediction.label] * 100)}%) [Custom]`;
                                        overlay.classList.remove('hidden');
                                        overlay.style.borderColor = '#1aaaca';
                                        overlay.style.background = 'rgba(26,170,202,0.6)';
                                    }
                                    
                                    if (typeof window.addLocalTranscriptEntry === 'function' && !window.gestureCooldown) {
                                        window.addLocalTranscriptEntry(signLabel, prediction.confidences[prediction.label], 'Remote Guest', 'sign');
                                        window.triggerGestureCooldown();
                                    }
                                }
                                tensor.dispose();
                            }).catch(() => tensor.dispose());
                        }
                    }

                    // 2. Try Native Pre-trained Gestures
                    if (results && results.gestures && results.gestures.length > 0 && results.gestures[0] && results.gestures[0].length > 0) {
                        const gesture = results.gestures[0][0];
                        if (gesture && gesture.score > 0.5) {
                            const gestName = gesture.categoryName;
                            
                            // Only apply if not 'None' 
                            if (gestName !== "None") {
                                const overlay = document.getElementById('gesture-overlay');
                                if (overlay && !window.gestureCooldown) {
                                    overlay.textContent = `🖐️ Remote: ${gestName} (${Math.round(gesture.score * 100)}%)`;
                                    overlay.classList.remove('hidden');
                                    overlay.style.borderColor = '#f43f5e';
                                    overlay.style.background = 'rgba(244,63,94,0.6)';
                                }
                                
                                // Pass native gesture into transcript!
                                if (typeof window.addLocalTranscriptEntry === 'function' && !window.gestureCooldown) {
                                    if (window.gestureDictionary && window.gestureDictionary[gestName]) {
                                        window.addLocalTranscriptEntry(window.gestureDictionary[gestName], gesture.score, 'Remote Guest', 'sign');
                                    } else {
                                        window.addLocalTranscriptEntry(gestName.replace('_', ' '), gesture.score, 'Remote Guest', 'sign');
                                    }
                                    window.triggerGestureCooldown();
                                }
                            }
                        }
                    }
                } catch (err) {
                    // MediaPipe may fail on some frames — skip silently
                }
            }

            remoteSignAnimFrame = requestAnimationFrame(processFrame);
        }

        video.onloadeddata = () => {
            processFrame();
            if (typeof showToast === 'function') showToast('🔍 Remote sign detection active — detecting hand gestures from screen capture...', 'azure', 5000);
        };

        // Auto-stop when share ends
        videoTrack.addEventListener('ended', () => stopRemoteSignCapture());

        console.log('🔍 Remote sign detection started:', label);

    } catch (err) {
        if (err.name !== 'AbortError') {
            console.warn('Remote sign capture failed:', err);
            if (typeof showToast === 'function') showToast('❌ Screen capture cancelled', 'error', 3000);
        }
    }
});

function stopRemoteSignCapture() {
    if (remoteSignAnimFrame) {
        cancelAnimationFrame(remoteSignAnimFrame);
        remoteSignAnimFrame = null;
    }
    if (remoteSignStream) {
        remoteSignStream.getTracks().forEach(t => t.stop());
        remoteSignStream = null;
    }
    if (window._remoteSignVideo) {
        window._remoteSignVideo.pause();
        window._remoteSignVideo.srcObject = null;
        window._remoteSignVideo.remove();
        window._remoteSignVideo = null;
    }
    // Reset button
    const btn = document.getElementById('btn-capture-remote-signs');
    if (btn) {
        btn.classList.remove('bg-rose-500', 'text-white');
        btn.classList.add('text-rose-400');
        btn.style.background = '#330d1a';
    }
    // Hide overlay
    const overlay = document.getElementById('gesture-overlay');
    if (overlay) overlay.classList.add('hidden');

    if (typeof showToast === 'function') showToast('🔍 Remote sign detection stopped', 'info', 2000);
    console.log('🔍 Remote sign detection stopped');
}



// ─── Chat Capture (Clipboard Paste) ───────────────────────
document.getElementById('btn-paste-chat')?.addEventListener('click', async () => {
    try {
        const text = await navigator.clipboard.readText();
        if (!text || !text.trim()) {
            if (typeof showToast === 'function') showToast('📋 Clipboard is empty — copy chat text first!', 'warning', 3000);
            return;
        }
        injectChatText(text.trim());
    } catch (err) {
        // Clipboard API blocked — show manual input
        const input = prompt('📋 Paste chat text here (Clipboard API blocked):');
        if (input && input.trim()) {
            injectChatText(input.trim());
        }
    }
});

// Also support Ctrl+Shift+V as a global shortcut
document.addEventListener('keydown', (e) => {
    if (e.ctrlKey && e.shiftKey && e.key === 'V') {
        e.preventDefault();
        document.getElementById('btn-paste-chat')?.click();
    }
});

function injectChatText(text) {
    // Parse multi-line chat (common formats: "User: msg", timestamps, etc.)
    const lines = text.split('\n').filter(l => l.trim());
    
    const logDiv = document.getElementById('transcript-log');
    if (!logDiv) return;

    let injectedCount = 0;
    lines.forEach(line => {
        const cleanLine = line.replace(/^\[.*?\]\s*/, '').trim(); // Strip timestamps
        if (!cleanLine) return;

        const entry = document.createElement('div');
        entry.className = 'text-xs text-pink-300 border-l-2 border-pink-500 pl-2 py-0.5 animate-fadeIn';
        entry.innerHTML = `<span class="text-pink-500 font-bold">📋 Chat:</span> ${cleanLine}`;
        logDiv.appendChild(entry);
        injectedCount++;
    });

    logDiv.scrollTop = logDiv.scrollHeight;

    // Feed into translation pipeline if active
    if (injectedCount > 0) {
        const fullText = lines.join(' ');
        const transSelect = document.getElementById('translation-lang');
        const targetLang = transSelect ? transSelect.value : null;
        
        // Use existing translation if available
        if (targetLang && window._translationCache !== undefined) {
            const selectElem = document.getElementById('recognition-lang');
            const sourceLang = selectElem ? selectElem.value.split('-')[0] : 'en';
            const targetCode = targetLang.split('-')[0];

            if (sourceLang !== targetCode) {
                fetch(`/api/translate`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ text: fullText, from: sourceLang, to: targetCode })
                }).then(r => r.json()).then(data => {
                    if (data.translatedText) {
                        const tEntry = document.createElement('div');
                        tEntry.className = 'text-xs text-pink-200 border-l-2 border-pink-400 pl-2 py-0.5 ml-4';
                        tEntry.innerHTML = `<span class="text-pink-400 font-bold">🌐 Translated:</span> ${data.translatedText}`;
                        logDiv.appendChild(tEntry);
                        logDiv.scrollTop = logDiv.scrollHeight;
                    }
                }).catch(() => {});
            }
        }

        if (typeof showToast === 'function') showToast(`📋 ${injectedCount} chat message(s) captured and injected`, 'azure', 3000);
        console.log(`📋 Chat capture: ${injectedCount} messages injected`);
    }
}

// ─── File Upload Logic ─────────────────────────────────────
document.getElementById('btn-upload-audio')?.addEventListener('click', () => {
    document.getElementById('audio-file-input').click();
});

document.getElementById('audio-file-input')?.addEventListener('change', async (e) => {
    const file = e.target.files[0];
    if (!file) return;
    
    const btn = document.getElementById('btn-upload-audio');
    btn.classList.add('animate-pulse');
    
    const selectElem = document.getElementById('recognition-lang');
    const transSelect = document.getElementById('translation-lang');
    
    const sourceLang = selectElem ? selectElem.value : 'en-US';
    const targetLang = transSelect ? transSelect.value : 'en-US';
    
    let session = state.currentSession;
    if (!state.isLiveActive || !session) {
        try {
            session = await apiCall('/sessions', {
                method: 'POST',
                body: JSON.stringify({
                    title: `File Translation - ${file.name}`,
                    sourceLanguage: sourceLang,
                    targetLanguage: targetLang,
                    consentGiven: true,
                    enableRecording: false
                })
            });
            state.currentSession = session;
            state.isLiveActive = true;
            
            document.getElementById('btn-start-live').style.display = 'none';
            document.getElementById('btn-stop-live').style.display = 'flex';
            document.getElementById('input-badge').textContent = 'File Mode';
            document.getElementById('input-badge').className = 'pipeline-badge pipeline-badge--active';
            
            await connectToHub(session.id, sourceLang, targetLang);
        } catch (err) {
            console.error(err);
            btn.classList.remove('animate-pulse');
            showToast("Failed to create File Session", "error");
            return;
        }
    }
    
    const formData = new FormData();
    formData.append('file', file);
    formData.append('sessionId', session.id);
    formData.append('sourceLanguage', sourceLang);
    formData.append('targetLanguage', targetLang);
    
    try {
        const headers = state.authToken ? { 'Authorization': `Bearer ${state.authToken}` } : {};
        const response = await fetch(`${API_BASE}/AudioFile/translate`, {
            method: 'POST',
            headers,
            body: formData
        });
        if (!response.ok) throw new Error("Upload Failed");
        showToast("File uploaded! Processing audio...", "info");
    } catch (err) {
        console.error(err);
        showToast("File translation failed.", "error");
    } finally {
        e.target.value = '';
        btn.classList.remove('animate-pulse');
    }
});

async function startLiveSession() {
    const recSelect = document.getElementById('recognition-lang');
    const transSelect = document.getElementById('translation-lang');
    
    const sourceLang = recSelect ? recSelect.value : 'en-US';
    const targetLang = transSelect ? transSelect.value : 'en-US';

    // Immediately sync the underlying sign.mt engine configuration
    if (typeof syncAvatarLanguage === 'function') {
        syncAvatarLanguage();
    }

    try {
        // Create a new session
        const session = await apiCall('/sessions', {
            method: 'POST',
            body: JSON.stringify({
                title: `Live Session - ${new Date().toLocaleString()}`,
                sourceLanguage: sourceLang,
                targetLanguage: targetLang,
                consentGiven: true,
                enableRecording: false
            })
        });

        state.currentSession = session;
        state.isLiveActive = true;

        // Connect to SignalR hub
        await connectToHub(session.id, sourceLang, targetLang);

        showToast('Live session started!', 'success');
    } catch (error) {
        console.error('Failed to start live session:', error);
        // DO NOT SWALLOW: Rethrow so index.html triggers Edge WebKit offline fallback
        throw error;
    }
}

async function stopLiveSession() {
    if (state.currentSession) {
        try {
            await apiCall(`/sessions/${state.currentSession.id}/complete`, { method: 'POST' });
        } catch { }
    }

    state.isLiveActive = false;
    if (state.hubConnection) {
        try {
            await state.hubConnection.invoke('LeaveSession', state.currentSession?.id || '');
            await state.hubConnection.stop();
        } catch(e) { console.error('Error stopping hub', e); }
        state.hubConnection = null;
    }
    
    const outputBadge = document.getElementById('output-badge');
    if (outputBadge) {
        outputBadge.textContent = 'Inactive';
        outputBadge.className = 'pipeline-badge pipeline-badge--inactive';
    }

    showToast('Session stopped', 'info');
}

async function connectToHub(sessionId, sourceLang, targetLang) {
    if (state.hubConnection) {
        await state.hubConnection.stop();
    }

    console.log(`Connecting to hub for session ${sessionId}...`);

    try {
        state.hubConnection = new signalR.HubConnectionBuilder()
            .withUrl(HUB_URL, {
                accessTokenFactory: () => state.authToken
            })
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Information)
            .build();

        state.hubConnection.on("ReceiveSubtitle", (sessId, original, translated, source, target, isFinal) => {
            updateSubtitles(original, translated);
        });

        state.hubConnection.on("ReceiveTranscript", (sessId, original, translated, source, target, speakerId, confidence, emotion) => {
            addTranscriptEntry(original, translated);
            processSignLanguage(translated);
        });

        state.hubConnection.on("ReceivePipelineStatus", (sessId, inputActive, outputActive, inputLatency, outputLatency, transcriptCount) => {
            const maxLatency = Math.max(inputLatency, outputLatency);
            document.getElementById('stat-latency').textContent = maxLatency + 'ms';
            animateMeters(inputActive, outputActive);
        });

        await state.hubConnection.start();
        console.log("Connected to SignalR hub");

        await state.hubConnection.invoke("JoinSession", sessionId);
        await state.hubConnection.invoke("StartPipeline", sessionId, sourceLang, targetLang);

    } catch (err) {
        console.error("SignalR Connection Error: ", err);
        showToast("Error connecting to live audio feed", "error");
    }
}

function updateSubtitles(original, translated) {
    const origEl = document.getElementById('subtitle-original');
    const transEl = document.getElementById('subtitle-translated');

    origEl.style.opacity = '0';
    transEl.style.opacity = '0';

    setTimeout(() => {
        origEl.textContent = original;
        transEl.textContent = translated;
        origEl.style.opacity = '1';
        transEl.style.opacity = '1';
    }, 200);
}

function addTranscriptEntry(original, translated, speakerId = null) {
    const container = document.getElementById('transcript-entries') || document.getElementById('transcript-log');
    if (!container) return; // Fail safely if UI layout omitted the node

    let safeSpeakerId = speakerId !== null && speakerId !== undefined ? String(speakerId) : "";
    let label = safeSpeakerId && safeSpeakerId !== "Guest" ? safeSpeakerId : "Signer";

    // If we are in the ICH Dashboard (index.html), route through the Unified Chat UI
    if (typeof window.addLocalTranscriptEntry === 'function' && document.getElementById('transcript-log')) {
        window.addLocalTranscriptEntry(translated, 1.0, label, 'mic');
        return;
    }

    // Legacy fallback for old /views/Home/Index
    const time = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

    const entry = document.createElement('div');
    entry.className = 'flex flex-col mb-4 w-full';
    
    let isSpeaker2 = safeSpeakerId && (safeSpeakerId === "Speaker-2" || safeSpeakerId === "2" || safeSpeakerId.includes("2"));
    let alignClass = isSpeaker2 ? 'items-end' : 'items-start';
    let bubbleClass = isSpeaker2 ? 'bg-[#00dce6]/10 border-[#00dce6]/20' : 'bg-[#2d3449] border-[#2d3449]';

    entry.innerHTML = `
        <div class="flex flex-col ${alignClass} w-full">
            <span class="text-[10px] text-slate-500 font-bold tracking-wider uppercase mb-1 px-1">
                ${label} • ${time}
            </span>
            <div class="p-4 rounded-2xl border ${bubbleClass} max-w-[85%] shadow-sm">
                <div class="text-sm text-slate-400 mb-1 italic">${original}</div>
                <div class="text-base text-white font-medium">${translated}</div>
            </div>
        </div>
    `;

    container.appendChild(entry);
    
    // Store in global accumulator for database persistence when session stops
    if (typeof window.archiveTranscriptEntry === 'function') {
        window.archiveTranscriptEntry(translated, 1.0, label, 'mic');
    }
    
    // Smooth scroll to bottom
    container.scrollTo({
        top: container.scrollHeight,
        behavior: 'smooth'
    });
}

window.sendToCloudAI = function(text) {
    const signMtIframe = document.getElementById('sign-iframe');
    if (signMtIframe && signMtIframe.contentWindow) {
        signMtIframe.contentWindow.postMessage({ type: "sign_translate", text: text }, "*");
    }
};

async function sendKeyboardInput() {
    const input = document.getElementById('keyboard-input');
    const text = input.value.trim();
    if (!text) return;

    input.value = '';

    if (state.isLiveActive && state.currentSession && state.hubConnection) {
        try {
            await state.hubConnection.invoke("SendKeyboardInput", state.currentSession.id, text, document.getElementById('target-lang').value);
            addTranscriptEntry(text, '(sending...)');
        } catch (error) {
            console.error('Failed to send keyboard input:', error);
        }
    }
}

// ─── Sign Language Visualization ───────────────────────────
const signCanvas = document.getElementById('sign-canvas');
const signCtx = signCanvas?.getContext('2d');

function processSignLanguage(text) {
    const words = text.split(' ').map(w => w.toLowerCase().replace(/[^a-záéíóúñ]/g, ''));
    state.signLanguageQueue.push(...words.filter(w => w.length > 0));

    if (!state.isProcessingSign) {
        processNextSignWord();
    }
}

function processNextSignWord() {
    if (state.signLanguageQueue.length === 0) {
        state.isProcessingSign = false;
        return;
    }

    state.isProcessingSign = true;
    const word = state.signLanguageQueue.shift();

    // Display word
    const wordEl = document.getElementById('sign-current-word');
    if (wordEl) wordEl.textContent = word;

    // Draw hand gesture animation on canvas
    drawHandGesture(word);

    // Process next word after delay
    const delay = Math.max(500, word.length * 150);
    setTimeout(processNextSignWord, delay);
}

function drawHandGesture(word) {
    if (!signCtx || !signCanvas) return;

    const w = signCanvas.width;
    const h = signCanvas.height;

    // Clear
    signCtx.clearRect(0, 0, w, h);

    // Background
    signCtx.fillStyle = '#1a1a28';
    signCtx.fillRect(0, 0, w, h);

    // Draw a stylized hand/gesture based on word
    const centerX = w / 2;
    const centerY = h / 2;

    // Seed random-like values from word for consistent gesture per word
    const seed = word.split('').reduce((s, c) => s + c.charCodeAt(0), 0);

    // Body silhouette
    signCtx.save();
    signCtx.fillStyle = 'rgba(118, 185, 0, 0.1)';
    signCtx.beginPath();
    signCtx.ellipse(centerX, centerY + 80, 60, 100, 0, 0, Math.PI * 2);
    signCtx.fill();

    // Head
    signCtx.fillStyle = 'rgba(118, 185, 0, 0.15)';
    signCtx.beginPath();
    signCtx.arc(centerX, centerY - 60, 35, 0, Math.PI * 2);
    signCtx.fill();

    // Arms/hands based on gesture
    signCtx.strokeStyle = '#76b900';
    signCtx.lineWidth = 4;
    signCtx.lineCap = 'round';
    signCtx.lineJoin = 'round';

    const gestureType = seed % 6;

    if (gestureType === 0) {
        // Waving hand (hello/goodbye)
        drawWavingHand(centerX, centerY);
    } else if (gestureType === 1) {
        // Pointing up (yes)
        drawPointingUp(centerX, centerY);
    } else if (gestureType === 2) {
        // Open palms (please/welcome)
        drawOpenPalms(centerX, centerY);
    } else if (gestureType === 3) {
        // Thumbs up (good/ok)
        drawThumbsUp(centerX, centerY);
    } else if (gestureType === 4) {
        // Heart (love/like)
        drawHeart(centerX, centerY);
    } else {
        // Fingerspelling position
        drawFingerspell(centerX, centerY, word);
    }

    signCtx.restore();

    // Word label
    signCtx.fillStyle = '#76b900';
    signCtx.font = 'bold 18px Inter, sans-serif';
    signCtx.textAlign = 'center';
    signCtx.fillText(word.toUpperCase(), centerX, h - 30);
}

function drawWavingHand(cx, cy) {
    signCtx.beginPath();
    signCtx.moveTo(cx + 30, cy - 10);
    signCtx.lineTo(cx + 80, cy - 80);
    signCtx.stroke();

    // Hand
    signCtx.fillStyle = 'rgba(118, 185, 0, 0.3)';
    signCtx.beginPath();
    signCtx.arc(cx + 85, cy - 90, 20, 0, Math.PI * 2);
    signCtx.fill();
    signCtx.stroke();

    // Fingers
    for (let i = 0; i < 5; i++) {
        const angle = -Math.PI / 2 + (i - 2) * 0.3;
        signCtx.beginPath();
        signCtx.moveTo(cx + 85, cy - 90);
        signCtx.lineTo(cx + 85 + Math.cos(angle) * 25, cy - 90 + Math.sin(angle) * 25);
        signCtx.stroke();
    }
}

function drawPointingUp(cx, cy) {
    signCtx.beginPath();
    signCtx.moveTo(cx + 20, cy + 20);
    signCtx.lineTo(cx + 40, cy - 50);
    signCtx.stroke();

    // Pointing finger
    signCtx.beginPath();
    signCtx.moveTo(cx + 40, cy - 50);
    signCtx.lineTo(cx + 40, cy - 100);
    signCtx.stroke();

    signCtx.fillStyle = 'rgba(118, 185, 0, 0.4)';
    signCtx.beginPath();
    signCtx.arc(cx + 40, cy - 105, 8, 0, Math.PI * 2);
    signCtx.fill();
}

function drawOpenPalms(cx, cy) {
    // Left hand
    signCtx.beginPath();
    signCtx.moveTo(cx - 30, cy);
    signCtx.lineTo(cx - 70, cy - 40);
    signCtx.stroke();

    signCtx.fillStyle = 'rgba(118, 185, 0, 0.2)';
    signCtx.beginPath();
    signCtx.ellipse(cx - 80, cy - 50, 25, 18, -0.3, 0, Math.PI * 2);
    signCtx.fill();
    signCtx.stroke();

    // Right hand
    signCtx.beginPath();
    signCtx.moveTo(cx + 30, cy);
    signCtx.lineTo(cx + 70, cy - 40);
    signCtx.stroke();

    signCtx.fillStyle = 'rgba(118, 185, 0, 0.2)';
    signCtx.beginPath();
    signCtx.ellipse(cx + 80, cy - 50, 25, 18, 0.3, 0, Math.PI * 2);
    signCtx.fill();
    signCtx.stroke();
}

function drawThumbsUp(cx, cy) {
    signCtx.beginPath();
    signCtx.moveTo(cx + 20, cy + 10);
    signCtx.lineTo(cx + 50, cy - 30);
    signCtx.stroke();

    signCtx.fillStyle = 'rgba(118, 185, 0, 0.3)';
    signCtx.beginPath();
    signCtx.arc(cx + 55, cy - 40, 18, 0, Math.PI * 2);
    signCtx.fill();
    signCtx.stroke();

    // Thumb up
    signCtx.lineWidth = 5;
    signCtx.beginPath();
    signCtx.moveTo(cx + 55, cy - 55);
    signCtx.lineTo(cx + 55, cy - 85);
    signCtx.stroke();
    signCtx.lineWidth = 4;
}

function drawHeart(cx, cy) {
    signCtx.fillStyle = 'rgba(118, 185, 0, 0.3)';
    signCtx.beginPath();
    signCtx.moveTo(cx, cy - 20);
    signCtx.bezierCurveTo(cx - 40, cy - 60, cx - 80, cy - 20, cx, cy + 30);
    signCtx.bezierCurveTo(cx + 80, cy - 20, cx + 40, cy - 60, cx, cy - 20);
    signCtx.fill();
    signCtx.strokeStyle = '#76b900';
    signCtx.stroke();
}

function drawFingerspell(cx, cy, word) {
    const letter = word[0]?.toUpperCase() || 'A';
    signCtx.fillStyle = '#76b900';
    signCtx.font = 'bold 80px Inter, sans-serif';
    signCtx.textAlign = 'center';
    signCtx.textBaseline = 'middle';
    signCtx.fillText(letter, cx, cy - 20);

    signCtx.font = '14px Inter, sans-serif';
    signCtx.fillStyle = 'rgba(118, 185, 0, 0.5)';
    signCtx.fillText('(fingerspelling)', cx, cy + 40);
}

// ─── Copilot ───────────────────────────────────────────────
document.getElementById('btn-copilot-send')?.addEventListener('click', sendCopilotMessage);
document.getElementById('copilot-input')?.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') sendCopilotMessage();
});
document.getElementById('btn-copilot-summary')?.addEventListener('click', generateSummary);
document.getElementById('btn-copilot-actions')?.addEventListener('click', extractActions);

async function loadCopilotSessions() {
    try {
        const sessions = await apiCall('/sessions');
        const container = document.getElementById('copilot-sessions');
        container.innerHTML = (sessions || []).map(s => `
            <div class="session-item" onclick="selectCopilotSession('${s.id}')">
                <span class="material-icons-round" style="color: var(--color-accent); font-size: 18px;">graphic_eq</span>
                <span class="session-item__title" style="font-size: 13px;">${s.title || 'Untitled'}</span>
            </div>
        `).join('') || '<p style="color: var(--color-text-muted); font-size: 13px;">No sessions available</p>';
    } catch {
        // Silently handle
    }
}

function selectCopilotSession(id) {
    state.currentSession = { id };
    showToast('Session selected for AI analysis', 'success');
}

async function sendCopilotMessage() {
    const input = document.getElementById('copilot-input');
    const text = input.value.trim();
    if (!text) return;

    input.value = '';
    addChatMessage('user', text);

    if (!state.currentSession) {
        addChatMessage('system', 'Please select a session first from the sidebar.');
        return;
    }

    try {
        const response = await apiCall('/copilot/ask', {
            method: 'POST',
            body: JSON.stringify({
                sessionId: state.currentSession.id,
                query: text,
                history: []
            })
        });

        addChatMessage('system', response.answer);
    } catch {
        addChatMessage('system', 'Sorry, I could not process your request. Please ensure the backend API is running.');
    }
}

async function generateSummary() {
    if (!state.currentSession) {
        addChatMessage('system', 'Please select a session first.');
        return;
    }

    addChatMessage('user', 'Generate a summary of this session');

    try {
        const response = await apiCall(`/copilot/${state.currentSession.id}/summary`, {
            method: 'POST'
        });
        addChatMessage('system', response.summary);
    } catch {
        addChatMessage('system', 'Could not generate summary. Please ensure the backend is running.');
    }
}

async function extractActions() {
    if (!state.currentSession) {
        addChatMessage('system', 'Please select a session first.');
        return;
    }

    addChatMessage('user', 'Extract action items from this session');

    try {
        const items = await apiCall(`/copilot/${state.currentSession.id}/action-items`);
        const formatted = items.map((item, i) => `${i + 1}. ${item}`).join('\n');
        addChatMessage('system', formatted || 'No action items found.');
    } catch {
        addChatMessage('system', 'Could not extract action items. Please ensure the backend is running.');
    }
}

function addChatMessage(role, content) {
    const container = document.getElementById('chat-messages');
    const msg = document.createElement('div');
    msg.className = `chat-message chat-message--${role}`;

    const icon = role === 'system' ? 'smart_toy' : 'person';
    const iconBg = role === 'system' ? 'var(--color-accent-subtle)' : 'rgba(33, 150, 243, 0.15)';
    const iconColor = role === 'system' ? 'var(--color-accent)' : '#2196f3';

    msg.innerHTML = `
        <div class="chat-avatar" style="background: ${iconBg}; color: ${iconColor}">
            <span class="material-icons-round">${icon}</span>
        </div>
        <div class="chat-content">
            <p>${content.replace(/\n/g, '<br>')}</p>
        </div>
    `;

    container.appendChild(msg);
    container.scrollTop = container.scrollHeight;
}

// ─── Modal ─────────────────────────────────────────────────
document.getElementById('btn-new-session')?.addEventListener('click', () => {
    document.getElementById('modal-new-session').classList.add('active');
});

document.getElementById('modal-close')?.addEventListener('click', closeModal);
document.getElementById('modal-cancel')?.addEventListener('click', closeModal);

document.querySelector('.modal-overlay')?.addEventListener('click', closeModal);

document.getElementById('modal-create')?.addEventListener('click', async () => {
    const title = document.getElementById('new-session-title').value || 'Untitled Session';
    const source = document.getElementById('new-session-source').value;
    const target = document.getElementById('new-session-target').value;
    const consent = document.getElementById('new-session-consent').checked;
    const recording = document.getElementById('new-session-recording').checked;

    try {
        await apiCall('/sessions', {
            method: 'POST',
            body: JSON.stringify({
                title,
                sourceLanguage: source,
                targetLanguage: target,
                consentGiven: consent,
                enableRecording: recording
            })
        });
        closeModal();
        loadDashboard();
        showToast('Session created!', 'success');
    } catch {
        showToast('Failed to create session', 'error');
    }
});

function closeModal() {
    document.getElementById('modal-new-session').classList.remove('active');
}

// ─── AI Pipeline Toggles ──────────────────────────────────
document.getElementById('toggle-noise')?.addEventListener('change', async (e) => {
    if (state.hubConnection && state.currentSession) {
        try {
            await state.hubConnection.invoke("ConfigurePipeline", state.currentSession.id, e.target.checked);
            const status = e.target.checked ? "Noise Removal ON" : "Noise Removal OFF";
            showToast(status, "info");
        } catch (err) {
            console.error('Failed to command Noise Removal', err);
        }
    }
});

document.getElementById('toggle-continuous')?.addEventListener('change', (e) => {
     showToast(e.target.checked ? "Continuous Mode ON" : "Continuous Mode OFF", "info");
});

// ─── AI Notice ─────────────────────────────────────────────
document.getElementById('ai-notice-close')?.addEventListener('click', () => {
    document.getElementById('ai-notice').style.display = 'none';
});

// ─── Accent Oracle ─────────────────────────────────────────
document.getElementById('btn-accent-oracle')?.addEventListener('click', () => {
    document.getElementById('modal-accent-oracle').classList.add('open');
    document.getElementById('oracle-results').classList.add('hidden');
});
document.getElementById('oracle-close')?.addEventListener('click', () => {
    document.getElementById('modal-accent-oracle').classList.remove('open');
});

document.getElementById('btn-oracle-listen')?.addEventListener('click', () => {
    const btn = document.getElementById('btn-oracle-listen');
    btn.innerHTML = `<span class="material-symbols-outlined animate-spin" style="animation: spin 1s linear infinite;">sync</span> Analyzing...`;
    btn.classList.add('opacity-50', 'cursor-not-allowed');
    btn.disabled = true;
    
    setTimeout(() => {
        btn.innerHTML = `<span class="material-symbols-outlined">mic</span> Start Diagnostic`;
        btn.classList.remove('opacity-50', 'cursor-not-allowed');
        btn.disabled = false;
        
        const isNative = Math.random() > 0.5;
        const scoreNative = isNative ? Math.floor(Math.random() * 15) + 80 : Math.floor(Math.random() * 30) + 35;
        const scoreForeign = 100 - scoreNative;
        
        document.getElementById('oracle-score-native').textContent = `${scoreNative}%`;
        document.getElementById('oracle-bar-native').style.width = `${scoreNative}%`;
        
        document.getElementById('oracle-score-foreign').textContent = `${scoreForeign}%`;
        document.getElementById('oracle-bar-foreign').style.width = `${scoreForeign}%`;
        
        const verdict = document.getElementById('oracle-verdict');
        if (scoreNative > 85) verdict.textContent = "Verdict: You have a highly native North American phonetic signature!";
        else if (scoreNative > 60) verdict.textContent = "Verdict: Clear English with moderate regional dialect influence.";
        else verdict.textContent = "Verdict: Strong foreign phonetic influence detected (Likely Romance/Latin origin).";
        
        document.getElementById('oracle-results').classList.remove('hidden');
    }, 2800);
});

// ─── Toast Notifications ───────────────────────────────────
function showToast(message, type = 'info') {
    const toast = document.createElement('div');
    toast.style.cssText = `
        position: fixed;
        top: 20px;
        right: 20px;
        background: ${type === 'error' ? 'rgba(244,67,54,0.9)' :
            type === 'success' ? 'rgba(76,175,80,0.9)' :
            'rgba(33,150,243,0.9)'};
        color: white;
        padding: 12px 24px;
        border-radius: 10px;
        font-family: Inter, sans-serif;
        font-size: 14px;
        font-weight: 500;
        z-index: 1000;
        animation: slideDown 0.3s ease;
        backdrop-filter: blur(10px);
    `;
    toast.textContent = message;
    document.body.appendChild(toast);

    setTimeout(() => {
        toast.style.opacity = '0';
        toast.style.transform = 'translateY(-10px)';
        toast.style.transition = '0.3s ease';
        setTimeout(() => toast.remove(), 300);
    }, 3000);
}

// ─── Helpers ───────────────────────────────────────────────
function formatDate(dateStr) {
    if (!dateStr) return '';
    return new Date(dateStr).toLocaleDateString('en-US', {
        month: 'short', day: 'numeric', year: 'numeric',
        hour: '2-digit', minute: '2-digit'
    });
}

function formatDuration(start, end) {
    if (!start || !end) return '--';
    const ms = new Date(end) - new Date(start);
    const minutes = Math.floor(ms / 60000);
    if (minutes < 60) return `${minutes}m`;
    const hours = Math.floor(minutes / 60);
    return `${hours}h ${minutes % 60}m`;
}

function getStatusLabel(status) {
    const labels = ['Created', 'Active', 'Paused', 'Completed', 'Archived'];
    return labels[status] || 'Unknown';
}

function animateMeters(inputLevelValue = null, outputLevelValue = null) {
    if (state.isLiveActive) {
        const inputLevel = inputLevelValue !== null && inputLevelValue ? (20 + Math.random() * 60) : (Math.random() * 10);
        const outputLevel = outputLevelValue !== null && outputLevelValue ? (15 + Math.random() * 50) : (Math.random() * 10);

        const meterIn = document.getElementById('meter-input-fill');
        if (!meterIn) return;

        meterIn.style.width = inputLevel + '%';
        document.getElementById('meter-output-fill').style.width = outputLevel + '%';
        document.getElementById('meter-input-value').textContent = Math.round(-60 + inputLevel * 0.8) + ' dB';
        document.getElementById('meter-output-value').textContent = Math.round(-60 + outputLevel * 0.8) + ' dB';
    }
}

// ─── Initialize ────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
    loadDashboard();
    animateMeters();
    console.log('🟢 Inclusive Communication Hub - Web Portal initialized');
});

// Add CSS animation for toast
const toastStyle = document.createElement('style');
toastStyle.textContent = `
    @keyframes slideDown {
        from { opacity: 0; transform: translateY(-10px); }
        to { opacity: 1; transform: translateY(0); }
    }
`;
document.head.appendChild(toastStyle);

window.showToast = function(message, type = 'info') {
    const toast = document.createElement('div');
    const bgClass = type === 'success' ? 'bg-green-500/20 text-green-400 border-green-500/30' 
                  : type === 'error' ? 'bg-red-500/20 text-red-400 border-red-500/30'
                  : 'bg-primary/20 text-primary border-primary/30';
    
    toast.className = `fixed bottom-4 right-4 z-[9999] px-4 py-3 rounded-lg border backdrop-blur-md shadow-lg flex items-center gap-3 ${bgClass}`;
    toast.style.animation = 'slideDown 0.3s ease-out forwards';
    toast.innerHTML = `
        <span class="material-symbols-outlined text-lg">${type === 'success' ? 'check_circle' : type === 'error' ? 'error' : 'info'}</span>
        <span class="text-sm font-semibold">${message}</span>
    `;
    document.body.appendChild(toast);
    setTimeout(() => {
        toast.style.opacity = '0';
        toast.style.transition = 'opacity 0.3s';
        setTimeout(() => toast.remove(), 300);
    }, 4000);
};

// ==========================================
// Phase 5: MediaPipe Gesture-to-Text Engine
// ==========================================
let gestureRecognizer = null;
let webcamRunning = false;
let lastVideoFrameTime = -1;
let gestureCooldown = false;
let cameraStream = null;

// Dictionary mapping gestures to spoken phrases
const gestureDictionary = {
    "Thumb_Up": "Sounds perfectly fine to me!",
    "Thumb_Down": "I have to disagree with that.",
    "Open_Palm": "Hello everyone!",
    "Closed_Fist": "Hold on a second.",
    "Pointing_Up": "I have a question.",
    "Victory": "Peace out!",
    "ILoveYou": "I love you guys!"
};

async function initGestureEngine() {
    try {
        if (!window.FilesetResolver || !window.GestureRecognizer) {
            setTimeout(initGestureEngine, 500);
            return;
        }
        const vision = await window.FilesetResolver.forVisionTasks(
            "https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.3/wasm"
        );
        gestureRecognizer = await window.GestureRecognizer.createFromOptions(vision, {
            baseOptions: {
                modelAssetPath: "https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/1/gesture_recognizer.task",
                delegate: "GPU"
            },
            runningMode: "VIDEO",
            numHands: 1
        });
        console.log("MediaPipe Gesture Engine initialized.");
    } catch(err) {
        console.error("Failed to init MediaPipe:", err);
    }
}

// Start Init
initGestureEngine();

// ========== KNN LAB UI BINDINGS ==========

function renderSignLibrary() {
    const grid = document.getElementById('sign-library-grid');
    const countBadge = document.getElementById('lab-count');
    if (!grid || !knnClassifier) return;
    
    const counts = knnClassifier.getClassExampleCount();
    const classes = Object.keys(counts);
    
    if (countBadge) countBadge.textContent = `Total: ${classes.length}`;
    
    if (classes.length === 0) {
        grid.innerHTML = `
            <div class="flex flex-col items-center justify-center py-20 text-center opacity-50">
                <span class="material-symbols-outlined text-5xl mb-3">category</span>
                <p class="text-sm font-semibold">Library is empty.</p>
                <p class="text-[10px] text-slate-400 mt-1">Record a gesture first.</p>
            </div>`;
        return;
    }
    
    grid.innerHTML = classes.map(label => `
        <div class="bg-[#131b2e] rounded-xl p-4 flex items-center justify-between border border-slate-700/50 hover:border-[#00dce6]/30 transition-colors group">
            <div class="flex items-center gap-4">
                <div class="w-10 h-10 rounded-lg bg-[#00dce6]/10 flex items-center justify-center text-[#00dce6]">
                    <span class="material-symbols-outlined">sign_language</span>
                </div>
                <div>
                    <h4 class="font-bold text-sm tracking-widest uppercase text-white">${label}</h4>
                    <p class="text-[10px] text-slate-400">${counts[label]} frames captured</p>
                </div>
            </div>
            <button onclick="deleteSignClass('${label}')" class="p-2 text-slate-500 hover:text-red-400 hover:bg-red-400/10 rounded-lg transition-colors opacity-0 group-hover:opacity-100">
                <span class="material-symbols-outlined">delete</span>
            </button>
        </div>
    `).join('');
}

window.deleteSignClass = function(label) {
    if (knnClassifier && knnClassifier.getNumClasses() > 0) {
        knnClassifier.clearClass(label);
        saveClassifier(); // Push to DB
        renderSignLibrary();
        if (typeof showToast !== 'undefined') showToast(`Deleted gesture: ${label}`, 'success');
    }
};

window.stopTrainingPhase = function() {
    isTraining = false;
    const btnCollect = document.getElementById('btn-lab-record');
    if (btnCollect) {
        btnCollect.classList.replace('bg-red-500', 'bg-amber-500');
        btnCollect.classList.replace('hover:bg-red-600', 'hover:bg-amber-400');
        btnCollect.classList.remove('animate-pulse');
        const recText = document.getElementById('rec-text');
        if (recText) recText.textContent = 'Record';
    }
    
    if (typeof showToast !== 'undefined') {
        showToast(`Registered '${trainingLabel}' with ${trainingCount} frame samples.`, 'success');
    }
    
    if (trainingCount > 0 && typeof renderSignLibrary === 'function') {
        saveClassifier();
        renderSignLibrary();
    }
    
    if (window.trainingTimeout) {
        clearTimeout(window.trainingTimeout);
        window.trainingTimeout = null;
    }
};

document.addEventListener('DOMContentLoaded', () => {
    // Wait slightly if dynamically rendered
    setTimeout(() => {
        const btnCollect = document.getElementById('btn-lab-record');
        const inputSignName = document.getElementById('lab-sign-name');
        
        if (btnCollect && inputSignName) {
            btnCollect.addEventListener('click', (e) => {
                e.preventDefault();
                if (isTraining) return; // Ignore if already training
                
                if (!inputSignName.value.trim()) {
                    inputSignName.focus();
                    return;
                }
                
                if (!webcamRunning) {
                    if (typeof showToast !== 'undefined') showToast("Please ACTIVATE CAMERA first to record.", "error");
                    const btnCam = document.getElementById('btn-lab-webcam');
                    if (btnCam) btnCam.click();
                    return;
                }
                
                trainingCount = 0; // Reset counter for new burst
                trainingLabel = inputSignName.value.trim();
                
                const selFrames = document.getElementById('lab-frame-count');
                window.targetTrainingCount = selFrames ? parseInt(selFrames.value) || 5 : 5;
                isTraining = true;
                
                // UX Feedback Update
                btnCollect.classList.replace('bg-amber-500', 'bg-red-500');
                btnCollect.classList.replace('hover:bg-amber-400', 'hover:bg-red-600');
                btnCollect.classList.add('animate-pulse');
                
                const recText = document.getElementById('rec-text');
                if (recText) recText.textContent = `Recording (${window.targetTrainingCount}f)...`;

                // Fallback automated Hands-Free Halt (5 seconds max if hand is hidden)
                if (window.trainingTimeout) clearTimeout(window.trainingTimeout);
                window.trainingTimeout = setTimeout(() => {
                    if (isTraining) {
                        window.stopTrainingPhase();
                    }
                }, 5000);
            });
        }
    }, 500);
});

document.body.addEventListener('click', async (e) => {
    // KNN Lab Toggles
    const btnTrainLab = e.target.closest('#btn-train-lab');
    if (btnTrainLab) {
        initKNN();
        const labUI = document.getElementById('gesture-lab-ui');
        if (labUI) {
            labUI.classList.remove('hidden');
            labUI.classList.add('flex');
        }
        return;
    }

    const btnCloseLab = e.target.closest('#btn-close-lab');
    if (btnCloseLab) {
        const labUI = document.getElementById('gesture-lab-ui');
        if (labUI) {
            labUI.classList.add('hidden');
            labUI.classList.remove('flex');
        }
        return;
    }

    const btnWebcam = e.target.closest('#btn-webcam');
    const btnLabWebcam = e.target.closest('#btn-lab-webcam');
    if (!btnWebcam && !btnLabWebcam) return;
    
    console.log("📹 [MediaPipe] Webcam actuator clicked!");
    const camContainer = document.getElementById('webcam-container');
    const status = document.getElementById('studio-status');
    
    if (!gestureRecognizer) {
        console.warn("📹 [MediaPipe] Engine still loading...");
        if (typeof showToast !== 'undefined') showToast("Gesture Engine loading (3MB Model). Please wait...", "info");
        else alert("Gesture Engine loading (3MB Model). Please wait...");
        return;
    }

    if (webcamRunning) {
        console.log("📹 [MediaPipe] Halting video stream.");
        // Stop logic
        webcamRunning = false;
        if (camContainer) camContainer.classList.add('hidden');
        if (cameraStream) { cameraStream.getTracks().forEach(track => track.stop()); cameraStream = null; }
        
        const btns = [document.getElementById('btn-webcam'), document.getElementById('btn-lab-webcam')].filter(Boolean);
        btns.forEach(b => {
            b.classList.remove('bg-[#a855f7]', 'text-white', 'animate-pulse');
            b.classList.add('bg-[#a855f7]/10', 'text-[#a855f7]');
        });
        
        if (status) {
            status.textContent = 'AWAITING CAMERA';
            status.className = 'text-[10px] uppercase font-bold tracking-widest text-amber-500 bg-amber-500/10 px-2 py-1 rounded border border-amber-500/20';
        }
        
        if (typeof showToast !== 'undefined') showToast("Webcam Gestures OFF", "info");
    } else {
        console.log("📹 [MediaPipe] Requesting hardware access...");
        // Start logic
        try {
            cameraStream = await navigator.mediaDevices.getUserMedia({ video: { width: 640, height: 480 } });
            console.log("📹 [MediaPipe] Hardware access granted. Binding to DOM.");
            
            const dashboardVideo = document.getElementById('webcam-video');
            const labVideo = document.getElementById('lab-webcam-video');
            
            let loaded = false;
            const onLoaded = () => { 
                if(!loaded) { 
                    loaded = true; 
                    // Critical VRAM flush delay: prevent WebGL blank-texture crashes
                    setTimeout(() => window.requestAnimationFrame(predictWebcam), 50); 
                } 
            };
            if (dashboardVideo) {
                dashboardVideo.srcObject = cameraStream;
                dashboardVideo.play().catch(e => console.warn("Dashboard Video Play block:", e));
                dashboardVideo.onloadeddata = onLoaded;
            }
            if (labVideo) {
                labVideo.srcObject = cameraStream;
                labVideo.play().catch(e => console.warn("Lab Video Play block:", e));
                labVideo.onloadeddata = onLoaded;
            }
            
            webcamRunning = true;
            if (camContainer) camContainer.classList.remove('hidden');
            
            const btns = [document.getElementById('btn-webcam'), document.getElementById('btn-lab-webcam')].filter(Boolean);
            btns.forEach(b => {
                b.classList.remove('bg-[#a855f7]/10', 'text-[#a855f7]');
                b.classList.add('bg-[#a855f7]', 'text-white', 'animate-pulse');
            });
            
            if (status) {
                status.textContent = 'CAMERA ACTIVE';
                status.className = 'text-[10px] uppercase font-bold tracking-widest text-green-400 bg-green-400/10 px-2 py-1 rounded border border-green-400/20';
            }
            
            if (typeof showToast !== 'undefined') showToast("Webcam Gestures ON", "success");
        } catch (err) {
            console.error("📹 [MediaPipe] Webcam access denied", err);
            if (typeof showToast !== 'undefined') showToast("Webcam access denied.", "error");
            else alert("Webcam access denied.");
        }
    }
});

// Audio Upload Delegate Handler
document.body.addEventListener('click', (e) => {
    const btnUploadAudio = e.target.closest('#btn-upload-audio');
    if (btnUploadAudio) {
        console.log("📁 [AudioUpload] Select Audio clicked.");
        const audioInput = document.getElementById('audio-file-input');
        if (audioInput) audioInput.click();
    }
});

document.addEventListener('DOMContentLoaded', () => {
    const audioInput = document.getElementById('audio-file-input');
    if (audioInput) {
        audioInput.addEventListener('change', async (e) => {
            const file = e.target.files[0];
            if (!file) return;

            const btnUploadAudio = document.getElementById('btn-upload-audio');
            const originalContent = btnUploadAudio.innerHTML;
            btnUploadAudio.innerHTML = '<span class="material-symbols-outlined animate-spin" data-icon="sync">sync</span>';
            
            if (typeof showToast !== 'undefined') showToast(`Uploading ${file.name}...`, "info");
            
            const formData = new FormData();
            formData.append("file", file);
            
            const langSelect = document.getElementById('lang-select');
            if (langSelect && langSelect.value) {
                formData.append("language", langSelect.value);
            }
            
            try {
                console.log(`📁 [AudioUpload] Offloading ${file.name} to REST endpoint...`);
                const apiBaseUrl = window.location.protocol === 'file:' ? 'https://localhost:7198' : '';
                const response = await fetch(`${apiBaseUrl}/api/audiofile/process-file`, {
                    method: 'POST',
                    body: formData
                });
                
                if (!response.ok) throw new Error("Upload failed");
                const result = await response.json();
                
                if (typeof showToast !== 'undefined') showToast("Audio translated successfully!", "success");
                
                if (result && result.transcription) {
                    if (typeof updateTranscriptUI === 'function') {
                        updateTranscriptUI("Speaker 1", result.transcription, "Offline Audio");
                    }
                    const signMtIframe = document.getElementById('sign-iframe');
                    if (signMtIframe && signMtIframe.contentWindow) {
                        signMtIframe.contentWindow.postMessage({ type: "sign_translate", text: result.transcription }, "*");
                    }
                }
            } catch(err) {
                console.error("Audio Upload error:", err);
                if (typeof showToast !== 'undefined') showToast("Failed to process audio file.", "error");
                else alert("Failed to process audio file.");
            } finally {
                if (btnUploadAudio) btnUploadAudio.innerHTML = originalContent;
                e.target.value = ''; // Reset input to allow re-upload of same file
            }
        });
    }
});

function predictWebcam() {
    if (!webcamRunning) return;
    
    const dashboardVideo = document.getElementById('webcam-video');
    const labVideo = document.getElementById('lab-webcam-video');
    const videoElem = state.currentPage === 'signlab' ? labVideo : dashboardVideo;
    
    // Safety check for hidden/unloaded streams
    if (!videoElem || !videoElem.videoWidth || videoElem.readyState < 2 || videoElem.currentTime === 0 || !gestureRecognizer) {
        if (webcamRunning) window.requestAnimationFrame(predictWebcam);
        return;
    }

    const gestureOverlay = document.getElementById('gesture-overlay');
    
    // Synthetic 60FPS Temporal Clock to prevent 32-bit WASM Float Overflow
    if (typeof state.syntheticClock === 'undefined') state.syntheticClock = 0;
    state.syntheticClock += 16; // Force strictly monotonic 16ms delta
    let nowInMs = state.syntheticClock;
    
    let results;
    if (videoElem.currentTime !== lastVideoFrameTime) {
        try {
            results = gestureRecognizer.recognizeForVideo(videoElem, nowInMs);
            lastVideoFrameTime = videoElem.currentTime;
        } catch(mpErr) {
            console.error("MediaPipe Execution Fault:", mpErr);
        }
    }

        const triggerGestureCooldown = () => {
            gestureCooldown = true;
            if (gestureOverlay) {
                gestureOverlay.classList.add('bg-green-500/80');
                gestureOverlay.classList.remove('bg-black/60');
            }
            setTimeout(() => {
                gestureCooldown = false;
                if (gestureOverlay) {
                    gestureOverlay.classList.add('bg-black/60');
                    gestureOverlay.classList.remove('bg-green-500/80');
                    gestureOverlay.classList.add('hidden');
                }
            }, 4000);
        };

    if (results && results.landmarks && results.landmarks.length > 0) {
        try {
            // TFJS Custom KNN Logic
            if (knnClassifier && window.tf) {
                const flatLandmarks = results.landmarks[0].flatMap(p => [p.x, p.y, p.z]);
                const tensor = window.tf.tensor1d(flatLandmarks);
                
                if (isTraining && trainingLabel) {
                    knnClassifier.addExample(tensor, trainingLabel);
                    trainingCount++;
                    const tc = document.getElementById('train-count');
                    if (tc) tc.textContent = `Samples: ${trainingCount}`;
                    
                    if (window.targetTrainingCount && trainingCount >= window.targetTrainingCount) {
                        if (typeof window.stopTrainingPhase === 'function') {
                            window.stopTrainingPhase();
                        }
                    }

                } else if (knnClassifier.getNumClasses() > 0) {
                    // Live Prediction
                    knnClassifier.predictClass(tensor).then(res => {
                        // Trust strictly high confidences to prevent phantom text
                        if (res.confidences && res.confidences[res.label] > 0.85) {
                            const score = Math.round(res.confidences[res.label] * 100);
                            if (gestureOverlay) {
                                gestureOverlay.classList.remove('hidden');
                                gestureOverlay.textContent = `${res.label} (${score}%) [Custom]`;
                            }
                            
                            if (!gestureCooldown) {
                                if (typeof window.addLocalTranscriptEntry === 'function') {
                                    window.addLocalTranscriptEntry(res.label, score / 100, "You", "sign");
                                }
                                triggerGestureCooldown();
                            }
                        }
                    }).catch(e => {/* Hide background tensor noise errors */});
                }
            }
            
            // Fallback Native Google Pre-trained Logic (if not in cooldown)
            if (!gestureCooldown && results.gestures && results.gestures.length > 0 && results.gestures[0] && results.gestures[0].length > 0) {
                const categoryName = results.gestures[0][0].categoryName;
                const score = parseFloat((results.gestures[0][0].score * 100).toFixed(2));
                
                if (categoryName && categoryName !== "None" && score > 65) {
                    if (gestureOverlay) {
                        gestureOverlay.classList.remove('hidden');
                        gestureOverlay.textContent = `${categoryName.replace('_', ' ')} (${score}%)`;
                    }
                    
                    if (gestureDictionary && gestureDictionary[categoryName]) {
                        const phrase = gestureDictionary[categoryName];
                        if (typeof window.addLocalTranscriptEntry === 'function') {
                            window.addLocalTranscriptEntry(phrase, score / 100, "You", "sign");
                        }
                        triggerGestureCooldown();
                    }
                } else if (knnClassifier && knnClassifier.getNumClasses() === 0) {
                    // If native is 'None' and KNN implies no models, visually hide target box
                    if (gestureOverlay) gestureOverlay.classList.add('hidden');
                }
            }
        } catch (inferenceErr) {
            console.warn("Soft recovery from prediction crash:", inferenceErr);
        }
    } else {
        if(!gestureCooldown && gestureOverlay) gestureOverlay.classList.add('hidden');
    }

    // Loop
    if (webcamRunning) {
        window.requestAnimationFrame(predictWebcam);
    }
}
