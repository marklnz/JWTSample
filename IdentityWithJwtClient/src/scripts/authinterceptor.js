(function () {

    var interceptor = function ($q, $location, $window, jwtHelper, $injector) {
        return {
            'request': function (config) {
                // add Authorization header if token is available
                var token = $window.localStorage["accessToken"];

                // do not add the header to this request if we are calling to the token api, or if we don't have the token stored! 
                if (config.url.indexOf("/api/token") == -1 && token) {
                    if (jwtHelper.isTokenExpired(token)) {
                        // The access token has expired so we need to request a new one using the token api. 
                        // The service for the token api will save the new token to local storage 
                        var authService = $injector.get('tokenapi');
                        return authService.getAccessToken($window.localStorage["currentUser"]).then(function (response) {
                            // On success, use the new access token to create an Authorization header and add it to the request 
                            var token = $window.localStorage["accessToken"];
                            config.headers.Authorization = 'Bearer ' + token;
                            return config;
                        }, function(response) {
                            // We got an error trying to retrieve the new access token. Clear out all the stored access 
                            // data (username and tokens) - i.e. log the user off - and present the logon view. 
                            authService.clearAccessStorage();
                            $location.path('/logon');
                        });
                    }
                    else {
                        // The access token is still valid so use it to create an Authorization header and add it to the request
                        config.headers.Authorization = 'Bearer ' + token;
                    }
                }
                return config;
            },
            'responseError': function (response) {
                // Check for auth errors returning from calls to the API
                // TODO: Should we deal with 403s here also?
                if (response.status === 401) {
                    // We've got a 401 error so clear the access data, and present the logon view to the user  
                    var authService = $injector.get('tokenapi');
                    authService.clearAccessStorage();
                    $location.path('/logon');
                }
                return $q.reject(response);
            }
        };
        
    };

    var app = angular.module("identityWithJwtClient");
    app.factory('authinterceptor', interceptor);

} ());
