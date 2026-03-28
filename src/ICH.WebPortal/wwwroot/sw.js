const CACHE_NAME = 'ich-dashboard-v1';
const ASSETS_TO_CACHE = [
    '/',
    '/index.html',
    '/js/app.js',
    '/css/styles.css',
    '/css/output.css', // Tailwind compiled
    'https://cdn.jsdelivr.net/npm/@tensorflow/tfjs@4.10.0/dist/tf.min.js',
    'https://cdn.jsdelivr.net/npm/@tensorflow-models/knn-classifier@1.2.2/dist/knn-classifier.min.js',
    'https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.3/vision_bundle.mjs',
    // We intentionally cache the heavy ML modules so the dashboard opens even during a total network failure.
];

// Install Event: Initialize Cache
self.addEventListener('install', (event) => {
    self.skipWaiting();
    event.waitUntil(
        caches.open(CACHE_NAME).then((cache) => {
            console.log('[Service Worker] Precaching critical dashboard assets');
            return cache.addAll(ASSETS_TO_CACHE);
        }).catch(err => console.warn('[Service Worker] Precache soft-failure:', err))
    );
});

// Activate Event: Cleanup Old Caches
self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys().then((keyList) => {
            return Promise.all(keyList.map((key) => {
                if (key !== CACHE_NAME) {
                    console.log('[Service Worker] Removing old cache', key);
                    return caches.delete(key);
                }
            }));
        })
    );
    self.clients.claim();
});

// Fetch Event: Cache-First Strategy for speed, Network-Fallback for dynamic requests
self.addEventListener('fetch', (event) => {
    // Only handle GET requests
    if (event.request.method !== 'GET') return;
    
    // Bypass SignalR WebSockets and .NET API endpoints seamlessly
    if (event.request.url.includes('/hub') || event.request.url.includes('/api/')) return;

    event.respondWith(
        caches.match(event.request).then((cachedResponse) => {
            if (cachedResponse) {
                return cachedResponse; // Return heavily cached asset instantly (0ms latency Edge AI)
            }
            
            return fetch(event.request).then((networkResponse) => {
                // Dynamically cache 3D avatar assets or future TF.js weights on-the-fly
                if (event.request.url.endsWith('.glb') || event.request.url.endsWith('.bin')) {
                    const responseClone = networkResponse.clone();
                    caches.open(CACHE_NAME).then((cache) => {
                        cache.put(event.request, responseClone);
                    });
                }
                return networkResponse;
            }).catch(() => {
                // Return offline fallback if network dies completely
                console.warn('[Service Worker] Network request failed and asset is not cached:', event.request.url);
            });
        })
    );
});
