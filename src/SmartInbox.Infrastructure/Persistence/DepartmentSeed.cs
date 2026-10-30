using SmartInbox.Domain;

namespace SmartInbox.Infrastructure.Persistence;

/// <summary>Departments of the fictional "Ministry of Digital Services – Demo".</summary>
public static class DepartmentSeed
{
    public static readonly Department[] All =
    [
        new() { Id = 1, NameEn = "Minister's Office", NameAr = "مكتب الوزير",
            Description = "Executive correspondence, official invitations, high-level government communication",
            Keywords = "minister, invitation, cabinet, ceremony, executive", Email = "minister.office@mds.demo", HeadEmail = "chief.of.staff@mds.demo",
            SignatoryTitleEn = "Director, Minister's Office", SignatoryTitleAr = "مدير مكتب الوزير" },
        new() { Id = 2, NameEn = "Legal Affairs", NameAr = "الشؤون القانونية",
            Description = "Contracts, lawsuits, legal notices, regulatory opinions",
            Keywords = "contract, court, lawsuit, legal, notice, regulation", Email = "legal@mds.demo", HeadEmail = "legal.head@mds.demo",
            SignatoryTitleEn = "Director, Legal Affairs", SignatoryTitleAr = "مدير إدارة الشؤون القانونية" },
        new() { Id = 3, NameEn = "Finance & Procurement", NameAr = "المالية والمشتريات",
            Description = "Invoices, payments, tenders, budgets, vendor matters",
            Keywords = "invoice, payment, tender, budget, purchase order, vendor", Email = "finance@mds.demo", HeadEmail = "finance.head@mds.demo",
            SignatoryTitleEn = "Director, Finance & Procurement", SignatoryTitleAr = "مدير إدارة المالية والمشتريات" },
        new() { Id = 4, NameEn = "Human Resources", NameAr = "الموارد البشرية",
            Description = "Recruitment, employee affairs, training, leave, secondments",
            Keywords = "employee, recruitment, training, leave, salary, secondment", Email = "hr@mds.demo", HeadEmail = "hr.head@mds.demo",
            SignatoryTitleEn = "Director, Human Resources", SignatoryTitleAr = "مدير إدارة الموارد البشرية" },
        new() { Id = 5, NameEn = "Information Technology", NameAr = "تقنية المعلومات",
            Description = "Systems, integrations, cybersecurity, IT service requests",
            Keywords = "system, integration, cybersecurity, software, network, data", Email = "it@mds.demo", HeadEmail = "it.head@mds.demo",
            SignatoryTitleEn = "Director, Information Technology", SignatoryTitleAr = "مدير إدارة تقنية المعلومات" },
        new() { Id = 6, NameEn = "Customer Happiness", NameAr = "إسعاد المتعاملين",
            Description = "Public complaints, service requests, citizen feedback",
            Keywords = "complaint, feedback, service request, customer, delay", Email = "customer.happiness@mds.demo", HeadEmail = "customer.head@mds.demo",
            SignatoryTitleEn = "Director, Customer Happiness", SignatoryTitleAr = "مدير إدارة إسعاد المتعاملين" },
        new() { Id = 7, NameEn = "Strategy & Performance", NameAr = "الاستراتيجية والأداء",
            Description = "Circulars, KPIs, reports, policies, strategic initiatives",
            Keywords = "circular, policy, KPI, report, strategy, initiative", Email = "strategy@mds.demo", HeadEmail = "strategy.head@mds.demo",
            SignatoryTitleEn = "Director, Strategy & Performance", SignatoryTitleAr = "مدير إدارة الاستراتيجية والأداء" },
    ];
}
