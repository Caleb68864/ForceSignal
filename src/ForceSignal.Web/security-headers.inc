# Response headers every location adds. nginx drops a parent's add_header lines the moment a
# location declares one of its own, so these are included per location rather than set once.
#
# The two placeholders are filled in at image build time from VITE_API_BASE_URL, the same value
# the app itself is built against; the second is the WebSocket form of it for the SignalR hub.
add_header X-Content-Type-Options "nosniff" always;
add_header Referrer-Policy "no-referrer" always;
add_header Permissions-Policy "camera=(), microphone=(), geolocation=()" always;
add_header X-Frame-Options "DENY" always;
add_header Content-Security-Policy "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' __API_ORIGIN__ __API_WS_ORIGIN__; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'" always;
