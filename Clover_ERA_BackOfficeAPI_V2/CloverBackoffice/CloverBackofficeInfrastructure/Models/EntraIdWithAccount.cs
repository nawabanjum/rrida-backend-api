
public class EntraIdWithAccount
{
    public string authority { get; set; }
    public string uniqueId { get; set; }
    public string tenantId { get; set; }
    public string[] scopes { get; set; }
    public Account account { get; set; }
    public string idToken { get; set; }
    public Idtokenclaims1 idTokenClaims { get; set; }
    public string accessToken { get; set; }
    public bool fromCache { get; set; }
    public DateTime expiresOn { get; set; }
    public DateTime extExpiresOn { get; set; }
    public string correlationId { get; set; }
    public string requestId { get; set; }
    public string familyId { get; set; }
    public string tokenType { get; set; }
    public string cloudGraphHostName { get; set; }
    public string msGraphHost { get; set; }
    public bool fromNativeBroker { get; set; }
}

public class Account
{
    public string homeAccountId { get; set; }
    public string environment { get; set; }
    public string tenantId { get; set; }
    public string username { get; set; }
    public string localAccountId { get; set; }
    public string name { get; set; }
    public string authorityType { get; set; }
    public Tenantprofiles tenantProfiles { get; set; }
    public Idtokenclaims idTokenClaims { get; set; }
    public string idToken { get; set; }
}

public class Tenantprofiles
{
}

public class Idtokenclaims
{
    public string aud { get; set; }
    public string iss { get; set; }
    public int iat { get; set; }
    public int nbf { get; set; }
    public int exp { get; set; }
    public string name { get; set; }
    public string nonce { get; set; }
    public string oid { get; set; }
    public string preferred_username { get; set; }
    public string rh { get; set; }
    public string sid { get; set; }
    public string sub { get; set; }
    public string tid { get; set; }
    public string uti { get; set; }
    public string ver { get; set; }
}

public class Idtokenclaims1
{
    public string aud { get; set; }
    public string iss { get; set; }
    public int iat { get; set; }
    public int nbf { get; set; }
    public int exp { get; set; }
    public string name { get; set; }
    public string nonce { get; set; }
    public string oid { get; set; }
    public string preferred_username { get; set; }
    public string rh { get; set; }
    public string sid { get; set; }
    public string sub { get; set; }
    public string tid { get; set; }
    public string uti { get; set; }
    public string ver { get; set; }
}

