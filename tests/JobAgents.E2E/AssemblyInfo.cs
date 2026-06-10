using Xunit;

// The E2E suite drives a single shared web app whose state lives on disk (results/profile.json,
// the model-config store). Running test classes in parallel lets one class's write race another's
// read — e.g. CvProfileTests saving a CV while JobHuntFormTests asserts the resume box is empty.
// Serialize the whole assembly so each test sees a clean, deterministic disk state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
