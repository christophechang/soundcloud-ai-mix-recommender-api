# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in this project, please report it privately rather than opening a public issue.

**Use GitHub's private vulnerability reporting:**
Repository → Security tab → "Report a vulnerability"

Please include:
- A description of the vulnerability and its potential impact
- Steps to reproduce
- Any suggested fixes if you have them

I'll aim to respond within a few days. This is a personal project, so please bear that in mind when setting expectations on response time.

## Scope

This is a personal portfolio project. It is a read-only recommendation API backed by a public SoundCloud RSS feed. There is no user authentication, no user data stored, and no payment processing.

In scope:
- The `/api/mixes/recommend` and `/api/catalog` endpoints
- Authentication or authorisation bypass (if any)
- Injection vulnerabilities

Out of scope:
- Rate limiting bypass (already mitigated)
- Denial of service against a free-tier App Service
- Social engineering

## Disclosure

I follow coordinated disclosure. Please allow reasonable time to address the issue before any public disclosure.
