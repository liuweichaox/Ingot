# Ingot Product Information Architecture

Ingot's public content follows one narrative: how plant data becomes a production history, how the production history supports process investigation, and how an investigation returns to original records.

## Public documentation structure

1. **Documentation home**: establish the complete flow and provide reading paths for different audiences.
2. **Product overview**: explain value, product components, users, and the path from data to conclusion.
3. **Use cases**: show value through yield, machine differences, tooling trends, abnormal workpieces, and process adjustments.
4. **How Ingot works**: explain collection, production context, production history, investigation, and evidence review.
5. **Ingot Chat**: explain suitable questions, the investigation flow, question patterns, and analysis evidence.
6. **Rollout**: begin with one plant question and cover the data checklist, connection validation, result review, and expansion.
7. **FAQ**: answer questions about product scope, users, data preparation, deployment, and analysis definitions.

Public navigation follows “Understand Ingot—Use Ingot—Adopt Ingot.” Search, sitemap, and previous/next navigation cover only these pages.

## Content layers

### Public product content

The website, README, and public documentation site serve people who want to understand, evaluate, and use Ingot. They use manufacturing language and focus on product value, capabilities, use cases, workflow, and rollout.

### Engineering maintenance material

Architecture notes, design records, data contracts, development guides, configuration references, industry samples, and ADRs remain in the repository for implementation and review. They stay outside public documentation navigation, search, and sitemap.

## Website structure

The website answers these questions in order:

1. What is Ingot?
2. Why does a production history matter?
3. Which core capabilities does the system provide?
4. Which plant questions can it answer?
5. How does data become an engineering conclusion?
6. Which workspaces make up the product?
7. How does a team start with one plant use case?

The website and public documentation use the same terminology and reading order. Website links lead to the corresponding documentation pages.

## Platform information architecture

Platform is organized around production objects and work:

- Workbench summarizes key state, data coverage, and recent production records.
- Operations and Traceability connect equipment, batches, workpieces, cycles, and stage curves.
- Quality connects inspection tasks, outcomes, plant attachments, and reviews.
- Analysis Center provides historical comparison, data health, and analysis plans.
- Data Assets maintain objects, recipes, process stages, parameter meaning, and collection tasks.
- Tooling maintains components, assemblies, installations, and usage history.
- Ingot Chat starts an investigation from the current object and page context.
- Administration provides operating metrics, logs, and event subscriptions.

Pages use a consistent “overview—filter—result—detail” hierarchy so users move from business objects into original records.

## Maintenance rules

- Update Chinese and English public pages as pairs.
- Keep the website, README, and documentation home on the same product position.
- Add a public page only when it shortens the understanding or rollout path.
- Maintain implementation material through repository-internal links.
- Public content states current capabilities, usage, and rollout conditions.
