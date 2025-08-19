import { themes as prismThemes } from "prism-react-renderer";

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: "KubeOps - Kubernetes Operators in .NET",
  tagline: "A .NET SDK for building Kubernetes operators",
  favicon: "img/favicon.ico",

  future: {
    v4: true,
  },

  url: "https://dotnet.github.io",
  baseUrl: "/dotnet-operator-sdk/",

  organizationName: "dotnet",
  projectName: "dotnet-operator-sdk",

  onBrokenLinks: "throw",
  onBrokenMarkdownLinks: "warn",

  i18n: {
    defaultLocale: "en",
    locales: ["en"],
  },

  presets: [
    [
      "classic",
      {
        docs: {
          sidebarPath: "./sidebars.js",
          editUrl: "https://github.com/dotnet/dotnet-operator-sdk/tree/main/docs/",
        },
        blog: false,
        theme: {
          customCss: "./src/css/custom.css",
        },
      },
    ],
  ],

  markdown: {
    mermaid: true,
  },

  themes: [
    "@docusaurus/theme-mermaid",
    [
      require.resolve("@easyops-cn/docusaurus-search-local"),
      /** @type {import("@easyops-cn/docusaurus-search-local").PluginOptions} */
      ({
        hashed: true,
        language: ["en"],
        indexPages: true,
      }),
    ],
  ],

  themeConfig: {
    image: "img/logo_big.png",
    navbar: {
      title: "KubeOps",
      logo: {
        alt: "dotnet-operator-sdk Logo",
        src: "img/logo.png",
      },
      items: [
        {
          type: "docSidebar",
          position: "left",
          label: "Documentation",
          sidebarId: "operator",
        },
        {
          type: "docSidebar",
          position: "left",
          label: "Packages",
          sidebarId: "packages",
        },
        {
          href: "https://dotnetfoundation.org/about/policies/code-of-conduct",
          label: "Code of Conduct",
          position: "left",
        },
        {
          href: "https://github.com/dotnet/dotnet-operator-sdk",
          label: "GitHub",
          position: "right",
        },
      ],
    },
    footer: {
      style: "dark",
      copyright: `${new Date().getFullYear()} - KubeOps Maintainers.`,
      links: [
        {
          title: "Docs",
          items: [
            {
              label: "Documentation",
              to: "/docs/operator",
            },
            {
              label: "Packages",
              to: "/docs/packages",
            },
          ],
        },
        {
          title: "Community",
          items: [
            {
              label: "Issues",
              href: "https://github.com/dotnet/dotnet-operator-sdk/issues",
            },
            {
              label: "Security",
              href: "https://github.com/dotnet/dotnet-operator-sdk/security",
            },
            {
              label: "Discord",
              href: "https://discord.gg/ucUcxpPW8P",
            },
          ],
        },
        {
          items: [
            {
              html: `
              <div>
                <a href="https://dotnetfoundation.org" target="_blank" rel="noreferrer noopener" style="display: inline-block;" aria-label="Supported by the .NET Foundation">
                  <img src="https://raw.githubusercontent.com/dotnet-foundation/swag/refs/heads/main/logo/dotnetfoundation_v4.svg" alt="Supported by the .NET Foundation" width="160" height="160" />
                </a>
              </div>
              <div>Supported by the <a href="https://dotnetfoundation.org">.NET Foundation</a></div>
              `,
            },
          ],
        },
      ],
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ["csharp", "bash"],
    },
  },
};

export default config;
