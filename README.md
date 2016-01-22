# JwtSample

A small sample that implements an OAUTH style flow, using both refresh tokens and access tokens

There are two components - the API server, and the client.

### IdentityWithJwt MVC6 API Server

This is the API server component. It is implemented in ASP.Net 5 MVC6, and uses EF7 for the persistence store. 

There are unit tests that test the token issuing functionality, and these use XUnit and Moq. Test data is provided using the EF7 in-memory database.
The server runs on .net Core currently, but the unit tests are built against the latest .net 4.6 framework

### IdentityWithJwtClient

This is the front end client. This is written in Javascript using Angular 1.4 currently, and I will be porting this to Typescript also. 
 UI fluff is courtesy of Bootstrap 3.3.6.
 
More documentation to come. 


 


