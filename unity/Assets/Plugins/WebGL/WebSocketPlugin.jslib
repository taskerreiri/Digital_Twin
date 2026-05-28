mergeInto(LibraryManager.library, {

    // DTサーバーのWebSocketに接続し、受信メッセージをUnityへ転送する
    DTConnect: function(urlPtr, callbackObjPtr, onMsgPtr, onStatusPtr) {
        var url = UTF8ToString(urlPtr);
        var objName = UTF8ToString(callbackObjPtr);
        var onMsg = UTF8ToString(onMsgPtr);
        var onStatus = UTF8ToString(onStatusPtr);

        if (window._dtWs) {
            try { window._dtWs.close(); } catch (e) {}
        }

        function connect() {
            var ws = new WebSocket(url);
            window._dtWs = ws;

            ws.onopen = function() {
                SendMessage(objName, onStatus, "connected");
            };
            ws.onmessage = function(ev) {
                SendMessage(objName, onMsg, ev.data);
            };
            ws.onclose = function() {
                SendMessage(objName, onStatus, "disconnected");
                // 自動再接続 (3秒後)
                if (window._dtReconnect !== false) {
                    setTimeout(connect, 3000);
                }
            };
            ws.onerror = function() {
                SendMessage(objName, onStatus, "error");
            };
        }

        window._dtReconnect = true;
        connect();
    },

    DTDisconnect: function() {
        window._dtReconnect = false;
        if (window._dtWs) {
            try { window._dtWs.close(); } catch (e) {}
            window._dtWs = null;
        }
    }
});
