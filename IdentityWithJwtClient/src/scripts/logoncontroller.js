(function() {
    
    var app = angular.module("identityWithJwtClient");
    
    var LogonController = function($window, $scope, tokenapi, $location){
        // Take the user's credentials and send them to the API for authentication
        // On successful response, save the tokens in local storage
        // On FAILED response, show error message to user
        
        $scope.logon = function(username, password){
            tokenapi.logon(username, password).then(onLogon, onError);
        };
        
        var onLogon = function(data){
            $location.path("/main");
        }
        
        var onError = function(reason){
            // Would be good to have access to the HTTP status code here
            $scope.error = "Logon was unsuccessful";
        }        
    };
    
    app.controller("LogonController", LogonController);
    
}());