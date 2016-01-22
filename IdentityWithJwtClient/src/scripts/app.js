(function () {

    var app = angular.module("identityWithJwtClient", ["ngRoute", "angular-jwt"]);

    app.config(function ($routeProvider, $httpProvider) {
        $routeProvider
            .when("/main", {
                templateUrl: "main.html",
                controller: "MainController"
            })
            .when("/logon", {
                templateUrl: "logon.html",
                controller: "LogonController"
            })
            .otherwise({ redirectTo: "/main" });
            
        $httpProvider.interceptors.push('authinterceptor');
    });
} ());