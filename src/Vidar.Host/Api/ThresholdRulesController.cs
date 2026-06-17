using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Host.Actors;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/threshold-rules")]
public sealed class ThresholdRulesController : ControllerBase
{
    private readonly IRequiredActor<ThresholdEvaluatorActor> _evaluatorProvider;

    public ThresholdRulesController(IRequiredActor<ThresholdEvaluatorActor> evaluatorProvider)
    {
        _evaluatorProvider = evaluatorProvider;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var evaluator = await _evaluatorProvider.GetAsync();
        var response = await evaluator.Ask<AllThresholdRulesResponse>(
            new GetAllThresholdRules(), TimeSpan.FromSeconds(3));
        return Ok(response.Rules);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateThresholdRuleRequest request)
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DeviceId = request.DeviceId,
            CapabilityKey = request.CapabilityKey,
            Operator = request.Operator,
            Value = request.Value,
            StringValue = request.StringValue,
            EventName = request.EventName,
            Enabled = request.Enabled
        };

        var evaluator = await _evaluatorProvider.GetAsync();
        evaluator.Tell(new AddThresholdRule(rule));

        return Created($"/api/threshold-rules/{rule.Id}", rule);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateThresholdRuleRequest request)
    {
        var rule = new ThresholdRule
        {
            Id = id,
            Name = request.Name,
            DeviceId = request.DeviceId,
            CapabilityKey = request.CapabilityKey,
            Operator = request.Operator,
            Value = request.Value,
            StringValue = request.StringValue,
            EventName = request.EventName,
            Enabled = request.Enabled
        };

        var evaluator = await _evaluatorProvider.GetAsync();
        evaluator.Tell(new UpdateThresholdRule(rule));

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var evaluator = await _evaluatorProvider.GetAsync();
        evaluator.Tell(new RemoveThresholdRule(id));
        return NoContent();
    }
}

public sealed class CreateThresholdRuleRequest
{
    public required string Name { get; set; }
    public Guid DeviceId { get; set; }
    public required string CapabilityKey { get; set; }
    public ThresholdOperator Operator { get; set; }
    public double Value { get; set; }
    public string? StringValue { get; set; }
    public required string EventName { get; set; }
    public bool Enabled { get; set; } = true;
}
