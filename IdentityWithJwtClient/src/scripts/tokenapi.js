(function () {

    var tokenapi = function ($http, $log, $window, jwtHelper) {
                       
        var apiClientId = "1";
        var clientSecret = "postmanSecret1";

        var logon = function (username, password) {
            var data = JSON.stringify({
                username: username,
                password: password,
                apiclientid: apiClientId,
                clientsecret: clientSecret
            });
            
            return $http.post("http://localhost:5000/api/token/Logon", data)
                .then(function (response) {
                    $window.localStorage.setItem("currentUser", username);
                    $window.localStorage.setItem("refreshToken", response.data.refreshtoken);
                    $window.localStorage.setItem("accessToken", response.data.accesstoken);
                    decodeAccessToken($window.localStorage["accessToken"]);
                                        
                    return response.data;
                });
        };

        var logoff = function (username) {
            return $http.post("http://localhost:5000/api/token/logoff", { "username": username, "apiclientid": apiClientId, "refreshtoken": $window.localStorage.getItem("refreshToken") })
                .then(function (response) {
                    clearAccessStorage();
                    return response.data;
                });
        };

        var getAccessToken = function (username) {
            return $http.post("http://localhost:5000/api/token/GetAccessToken", { "username": username, "apiclientid": apiClientId, "refreshtoken": $window.localStorage.getItem("refreshToken") })
                .then(function (response) {
                    $window.localStorage.setItem("accessToken", response.data.accesstoken);
                    decodeAccessToken($window.localStorage["accessToken"]);
                    
                    return response.data;
                });
        };

        function decodeAccessToken(token){
            //TODO: sort out what we do with the token now - do we pull out the relevant claims and store those also? There are a few things we could do here, hence pulling it out into a separate fn
            var tokenPayload = jwtHelper.decodeToken(token);
            return tokenPayload; 
        }
        
        var clearAccessStorage = function() {
            $window.localStorage.removeItem("currentUser");
            $window.localStorage.removeItem("refreshToken");
            $window.localStorage.removeItem("accessToken");
        }
        
        return {
            logon: logon,
            logoff: logoff,
            getAccessToken: getAccessToken,
            clearAccessStorage: clearAccessStorage
        };
    }

    var module = angular.module("identityWithJwtClient");
    module.factory("tokenapi", tokenapi);

} ());