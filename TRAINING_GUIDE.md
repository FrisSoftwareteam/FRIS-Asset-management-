# FRISL Enterprise Asset Management System
## Training Guide

## Introduction
This training guide is designed for client stakeholders, project owners, administrators, and operational users of the FRISL Enterprise Asset Management System (EAMS).

Its purpose is to provide a clear understanding of:
- what the application is designed to do
- how the major modules work
- how users should navigate the system
- what has already been implemented
- what remains for full production readiness

The current solution is a working ASP.NET Core 10 web application built to demonstrate and support asset lifecycle management from registration through assignment, repair, loan, audit, and reporting.

## Solution Overview
FRISL EAMS helps the organization manage physical assets in a structured, auditable way.

The application supports:
- centralized asset registration
- asset tracking by department, location, and custodian
- lifecycle status control
- assignment and receipt confirmation
- repair and replacement workflows
- internal and external loan processing
- RFID-based movement monitoring
- audit session management
- operational and management reporting

## Technology Summary
The application is built with:
- ASP.NET Core MVC for workflow screens
- Blazor Server for dashboard interaction
- Entity Framework Core for data access
- SQLite as the current database
- .NET 10 runtime

Current local runtime details:
- Application folder: `FrislEams.Web`
- Database file: `FrislEams.Web/eams.db`
- Default URL: `http://localhost:5000`

## Business Value
The solution is intended to improve visibility, accountability, and control across the asset management process.

It helps answer key operational questions such as:
- What assets are currently owned by the organization?
- Where is each asset located?
- Who is responsible for each asset?
- What is the current condition and lifecycle status of each item?
- Which assets are under repair, on loan, pending replacement, or due for disposal?
- Which items triggered RFID movement alerts?
- What is the current asset value and depreciation profile?

## Access and Demo Users
The current version includes demo login credentials for training and demonstration purposes.

Available users:
- `admin / admin123`
- `auditor / audit123`
- `staff / staff123`
- `viewer / viewer123`

Current role intent:
- `Admin`: registration, approvals, status changes, high-level control actions
- `Auditor`: audit session activities
- `Staff`: operational participation in request and workflow submission
- `Viewer`: read-only review

Important note:
- the current authentication model is suitable for demo and pilot use, not for production-grade identity management

## Core Functional Concept
The most important concept in the system is the asset lifecycle.

Assets do not move freely between arbitrary states. They move through controlled status transitions, which helps preserve accountability and auditability.

Examples of lifecycle statuses include:
- `UnregisteredUnassigned`
- `RegisteredUnassigned`
- `AssignedPendingConfirmation`
- `ActiveAssigned`
- `Damaged`
- `UnderRepair`
- `PendingReplacement`
- `LoanedOutInternal`
- `LoanedOutExternal`
- `Retired`
- `Sold`
- `Discarded`

Each valid transition is recorded in asset history, allowing users and reviewers to track who changed what and why.

## End-to-End Process Flow
A typical asset journey in the system is:
1. An asset is registered.
2. A unique RFID code is linked to the asset.
3. The system generates an official FRISL asset tag code.
4. The asset becomes available for assignment.
5. Assignment is initiated to a user, department, and location.
6. The receiving user confirms receipt.
7. The asset becomes actively assigned.
8. The asset may later move through repair, loan, audit, replacement, or disposal workflows.
9. All key transitions are stored in immutable history.

This is the primary business story to present during training.

## Module Guide

### 1. Dashboard
Route:
- `/`

Purpose:
- provides a management summary of assets and workflow activity
- serves as the landing page after login

What users should understand:
- the dashboard gives a quick overview of the current state of the asset environment
- it is intended for operational monitoring and decision support

Best suited for:
- project owners
- administrators
- operations leads
- executive reviewers

### 2. Asset Register
Routes:
- `/Assets`
- `/Assets/Register`
- `/Assets/Detail/{id}`
- `/Assets/History/{id}`

Purpose:
- stores and displays all registered assets
- supports searching and filtering
- allows users to inspect detailed asset information and lifecycle history

Key capabilities:
- search by asset name, tag code, or serial number
- filter by status, category, and department
- view linked assignment records
- view repair history
- view RFID tag linkage
- view status history

Registration process:
- an administrator enters asset details
- the RFID code is validated for uniqueness
- the system generates a tag code using the format `FRISL-YEAR-CAT-DEPT-SEQ`
- the asset is registered and moved into an assignable state

### 3. Assignments
Routes:
- `/Assignments`
- `/Assignments/Create`

Purpose:
- manages allocation of assets to staff, departments, and locations

Key control feature:
- two-step custody flow

How it works:
- assignment is initiated by the responsible user
- the recipient confirms receipt
- the asset only becomes fully active after confirmation

Business benefit:
- strengthens accountability
- provides evidence of handover and receipt
- improves traceability of custodian responsibility

### 4. Asset Requests
Route:
- `/Workflow/Requests`

Purpose:
- allows users to request new or replacement assets

Current workflow:
- request submission
- department-level approval
- admin approval
- fulfilment pending status
- rejection with reason where necessary

Business value:
- supports internal demand management
- provides visibility into asset needs before fulfilment

### 5. Repairs and Replacement Workflow
Route:
- `/Workflow/Repairs`

Purpose:
- manages reported faults, maintenance actions, replacement decisions, and discard decisions

Current workflow:
- repair issue is raised against an asset
- admin reviews the request
- action is approved as repair, replacement, or discard
- contractor may be assigned
- asset lifecycle status is updated accordingly
- completed repairs return the asset to active service

Business value:
- formalizes maintenance decisions
- links fault handling to lifecycle governance
- supports contractor involvement and status visibility

### 6. Loan Management
Route:
- `/Workflow/Loans`

Purpose:
- manages temporary internal and external movement of assets

Current workflow:
- loan request is raised
- approval is completed by an authorized user
- asset status changes to internal or external loan state
- external loans automatically create an exit grant
- return closes the loan and restores the active asset state

Business value:
- provides a controlled process for temporary asset movement
- supports stronger oversight for items leaving standard custody

### 7. RFID Monitoring
Route:
- `/Workflow/Rfid`

Purpose:
- tracks RFID scan events at monitored doors or checkpoints

Current logic:
- unknown RFID tags trigger alerts
- assets with valid exit authorization are treated as authorized
- assets without valid authorization trigger unauthorized movement alerts

Business value:
- introduces a monitoring and control layer for physical asset movement
- supports exception handling and potential security review

Important note:
- the current solution includes the application logic for RFID processing, but physical device integration remains a next-phase activity

### 8. Audit Module
Routes:
- `/Audit`
- `/Audit/Variance/{id}`

Purpose:
- supports asset verification exercises and exception analysis

Current capabilities:
- start audit sessions
- submit audit results
- close audit sessions
- review variances between expected and observed records

Business value:
- supports internal audit and control processes
- enables structured review rather than manual reconciliation

### 9. Reports
Routes:
- `/Reports`
- `/Reports/Depreciation`
- `/Reports/Aging`

Purpose:
- provides operational and financial-style views of asset data

Current outputs:
- filtered asset report
- depreciation report
- aging report
- CSV export for major reports

Business value:
- supports management review
- supports downstream use in Excel and reporting tools
- provides visibility into asset age, value, and condition

### 10. Staff and Contractor Management
Routes:
- `/Staff`
- `/Contractors`

Purpose:
- maintains reference records used across operational workflows

Current capabilities:
- create and list staff members
- create, edit, activate, and deactivate repair contractors

Business value:
- improves data consistency in assignments and repairs
- supports cleaner workflow execution through maintained reference data

## Recommended Training Walkthrough
A practical client demo can follow this sequence:
1. Log in as `admin`.
2. Open the dashboard and explain the high-level metrics.
3. Navigate to the asset register and demonstrate search and filters.
4. Open an asset detail page and review its history.
5. Register a new asset and explain the generated tag code.
6. Initiate an assignment and explain pending confirmation.
7. Confirm the assignment to complete custody transfer.
8. Raise or review a repair request.
9. Approve a loan and explain the exit grant concept.
10. Open the RFID monitoring page and explain alert logic.
11. Start or review an audit session.
12. Export a report to CSV.

## Demo and Seed Data
The application currently includes seeded demonstration data to support walkthroughs and stakeholder review.

This includes sample:
- asset categories
- departments
- locations
- staff
- suppliers
- repair contractors
- registered assets
- assignments
- repair records
- requests
- loans
- RFID events
- audit sessions and results

This allows the system to be demonstrated immediately after startup.

## Running the Application
From the repository root:

```bash
cd FrislEams.Web
dotnet restore
dotnet run
```

Open in the browser:

```text
http://localhost:5000
```

Operational notes:
- the database is created automatically if required
- seed data is created automatically
- deleting `FrislEams.Web/eams.db` resets the demo environment

## Current Scope Delivered
The following capabilities are currently available in the application:
- asset registration with automatic tag generation
- RFID uniqueness validation
- lifecycle-driven asset status control
- assignment initiation and confirmation workflow
- repair, replacement, and discard decision handling
- internal and external loan workflow
- exit grant creation for external loans
- RFID alert logic for authorized and unauthorized movement
- audit session and variance handling
- reporting with CSV export
- staff and contractor setup screens
- seeded demonstration data

## What Is Still Outstanding
The current system is strong for demonstration, pilot usage, and workflow validation. However, several items remain before full production deployment.

### 1. Enterprise Authentication and Authorization
Still required:
- integration with a production identity platform such as ASP.NET Identity, Active Directory, or SSO
- secure user management
- stronger role and permission enforcement
- policy-based authorization across all modules

### 2. Security Hardening
Still required:
- stronger credential and access controls
- secure configuration management
- production HTTPS setup
- auditability for privileged actions

### 3. Procurement and Fulfilment Completion
Still required:
- stronger linkage between approved requests and actual procurement/fulfilment steps
- automated transition from approved request to delivered asset onboarding

### 4. Physical RFID Integration
Still required:
- integration with live RFID devices/readers
- real-time message transport and reliability handling
- production alert escalation workflows

### 5. Notifications and Escalation
Still required:
- UI notification center
- email, SMS, or collaboration-tool alerts
- acknowledgment and escalation workflows

### 6. Master Data Expansion
Still required:
- fuller administration for categories, suppliers, departments, and locations
- stronger validation and duplicate prevention across all reference records

### 7. Testing and Release Readiness
Still required:
- automated test coverage
- integration and regression testing
- user acceptance test scripts
- deployment pipeline and release process

### 8. Production Database Strategy
Still required:
- migration from SQLite to a production-grade database where appropriate
- backup, restore, and performance planning
- formal migration and deployment scripts

### 9. Documentation and SOPs
Still required:
- role-based user manuals
- administrator SOPs
- support and troubleshooting guidance
- governance and operational ownership documentation

## Recommended Next Phase Priorities
Recommended next steps for production readiness:
1. Implement enterprise authentication and authorization.
2. Move to a production-ready database platform.
3. Complete procurement-to-fulfilment linkage.
4. Integrate physical RFID infrastructure.
5. Add notifications, testing, and deployment automation.
6. Expand user and administrator documentation.

## Conclusion
The FRISL Enterprise Asset Management System already demonstrates a strong functional foundation for managing assets in a controlled and auditable way.

It provides:
- structured lifecycle management
- accountability for asset custody
- formal repair and loan processes
- RFID-based movement logic
- audit support
- useful management reporting

In summary:
- the core workflow design is in place
- the solution is suitable for demonstration and pilot discussions
- the next phase should focus on enterprise hardening, integration, and operational maturity
