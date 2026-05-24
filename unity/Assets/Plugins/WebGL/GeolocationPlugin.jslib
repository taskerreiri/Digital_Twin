mergeInto(LibraryManager.library, {

    StartGeolocation: function(callbackObjPtr, callbackMethodPtr) {
        var objName = UTF8ToString(callbackObjPtr);
        var methodName = UTF8ToString(callbackMethodPtr);

        if (!navigator.geolocation) {
            SendMessage(objName, methodName, "ERROR:Geolocation not supported");
            return;
        }

        if (window._geoWatchId != null) {
            navigator.geolocation.clearWatch(window._geoWatchId);
        }

        window._geoWatchId = navigator.geolocation.watchPosition(
            function(pos) {
                var data = pos.coords.latitude + "," +
                           pos.coords.longitude + "," +
                           (pos.coords.altitude || 0) + "," +
                           pos.coords.accuracy + "," +
                           pos.timestamp;
                SendMessage(objName, methodName, data);
            },
            function(err) {
                SendMessage(objName, methodName, "ERROR:" + err.message);
            },
            { enableHighAccuracy: true, maximumAge: 1000, timeout: 10000 }
        );
    },

    StopGeolocation: function() {
        if (window._geoWatchId != null) {
            navigator.geolocation.clearWatch(window._geoWatchId);
            window._geoWatchId = null;
        }
    }
});
