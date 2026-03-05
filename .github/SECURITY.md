# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in this project, please report it privately rather than opening a public issue.

**Preferred method:** GitHub Private Vulnerability Reporting  
Go to the repository → **Security** tab → **Report a vulnerability**

If you cannot use GitHub's reporting feature, please contact:  
**info@changsta.com**

When reporting a vulnerability, please include:

- A description of the vulnerability and its potential impact  
- Steps to reproduce the issue  
- Proof of concept or example requests if possible  
- Any suggested fixes or mitigation ideas (optional)

I will acknowledge reports when I can. This is a personal project, so response times may vary.

---

## Scope

This project is a **personal portfolio recommendation API** backed by a **public SoundCloud RSS feed**.

The application:

- Stores **no user data**
- Has **no authentication system**
- Performs **no payment processing**

### In Scope

Security issues affecting the API itself, including:

- `/api/mixes/recommend`
- `/api/catalog`
- Injection vulnerabilities (SQL, command, template, etc.)
- Server-side request forgery (SSRF)
- Deserialization vulnerabilities
- Security misconfiguration that could expose secrets or infrastructure
- Unexpected access to non-public endpoints or internal resources

### Out of Scope

The following are generally out of scope:

- Denial-of-service or load testing against the service
- Social engineering attacks
- Vulnerabilities in third-party services or providers outside this project

---

## Testing Guidelines

Please follow responsible testing practices:

- Do not intentionally degrade service availability
- Avoid generating excessive traffic
- Limit testing to requests you control
- Do not attempt to access or modify data that does not belong to you

---

## Disclosure

This project follows **coordinated disclosure**.

Please allow reasonable time for the issue to be investigated and resolved before making the vulnerability public. If a disclosure timeline is needed, it can be agreed upon during the reporting process.
