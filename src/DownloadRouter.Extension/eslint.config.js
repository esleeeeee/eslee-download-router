import eslint from "@eslint/js";
import tseslint from "typescript-eslint";

export default tseslint.config(
  {
    ignores: ["dist/**", "build-tests/**", "node_modules/**"],
  },
  eslint.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ["eslint.config.js", "scripts/**/*.mjs"],
    languageOptions: {
      globals: {
        URL: "readonly",
      },
    },
  },
  {
    files: ["**/*.ts"],
    languageOptions: {
      parserOptions: {
        project: ["./tsconfig.json", "./tsconfig.test.json"],
        tsconfigRootDir: import.meta.dirname,
      },
      globals: {
        chrome: "readonly",
        navigator: "readonly",
        crypto: "readonly",
      },
    },
    rules: {
      "@typescript-eslint/consistent-type-imports": "error",
      "@typescript-eslint/no-floating-promises": "error",
      "@typescript-eslint/no-explicit-any": "error"
    },
  },
);
