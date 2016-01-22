(function () {

    var valuesapi = function ($http, $log) {

        var getValues = function () {
            return $http.get("http://localhost:5000/api/Values")
                .then(function (response) {
                    return response.data;
                });
        };
        
        var getValue = function (id) {
            return $http.get("http://localhost:5000/api/Values/" + id)
                .then(function (response) {
                    return response.data;
                });
        };

        return {
            getValues: getValues,
            getValue: getValue
        };
    }

    var module = angular.module("identityWithJwtClient");
    module.factory("valuesapi", valuesapi);

} ());
