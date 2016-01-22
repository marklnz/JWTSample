(function () {

    var app = angular.module("identityWithJwtClient");

    var MainController = function ($window, $location, $scope, tokenapi, valuesapi) {

        $scope.accessToken = $window.localStorage["accessToken"]; 
        
        $scope.logoff = function () {
            tokenapi.logoff($window.localStorage.getItem("currentUser")).then(onLogoff, onError);
        };

        $scope.getAccessToken = function () {
            tokenapi.getAccessToken($window.localStorage.getItem("currentUser")).then(onTokenRefreshed, onError);
        }

        $scope.getValues = function () {
            valuesapi.getValues().then(onValuesReturned, onError);
        }
                
        $scope.getSingleValue = function () {
            valuesapi.getValue("1").then(onSingleValueReturned, onError);
        }

        var onLogoff = function (data) {
            $location.path("/loggedoff");
        }
        
        var onTokenRefreshed = function (data) {
            $location.path("/tokenRefreshed");
        }

        var onValuesReturned = function (data) {
            $scope.values = data;
        }

        var onSingleValueReturned = function (data) {
            $scope.singlevalue = data;
        }

        var onError = function (reason) {
            // Would be good to have access to the HTTP status code here
            $scope.error = "Logoff was unsuccessful";
        }

        if (!$window.localStorage || !$window.localStorage.getItem("refreshToken"))
            // show the logon view    
            $location.path("/logon");

        
    };

    app.controller("MainController", MainController);

} ());